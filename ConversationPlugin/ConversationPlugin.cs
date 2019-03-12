using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Neo.Core.Authentication;
using Neo.Core.Communication;
using Neo.Core.Communication.Packages;
using Neo.Core.Extensibility;
using Neo.Core.Extensibility.Events;
using Neo.Core.Management;
using Neo.Core.Networking;
using Neo.Core.Shared;
using Newtonsoft.Json;

namespace ConversationPlugin
{
    public class ConversationPlugin : Plugin
    {
        public override string Namespace => "ml.festival.conversation";

        private List<Conversation> conversations = new List<Conversation>();
        private Member pluginMember;
        private string databasePath;

        [EventListener(EventType.BeforeChannelJoin)]
        public override async Task OnBeforeChannelJoin(Before<JoinElementEventArgs<Channel>> args) {
            if (args.Event.Element.Attributes.ContainsKey("neo.origin") && args.Event.Element.Attributes["neo.origin"].ToString() == Namespace && args.Event.Element.Id.StartsWith("~conversation-")) {
                args.Cancel = true;

                var conversation = conversations.Find(_ => _.Channel.InternalId.Equals(args.Event.Element.InternalId));

                if (conversation == null) {
                    conversation = new Conversation(Guid.Parse(args.Event.Element.Id.Split('+')[1]), Guid.Parse(args.Event.Element.Id.Split('+')[2]), args.Event.Element);
                    conversations.Add(conversation);
                }

                if (conversation.Users.Contains(args.Event.Joiner.InternalId)) {
                    args.Event.Joiner.MoveToChannel(conversation.Channel, true);
                } else {
                    Logger.Instance.Log(LogLevel.Error, $"{args.Event.Joiner.Identity.Name} (@{args.Event.Joiner.Identity.Id}) is not part of this conversation.");
                }
            }
        }

        [EventListener(EventType.BeforeInput)]
        public override async Task OnBeforeInput(Before<InputEventArgs> args) {
            if (args.Event.Input.StartsWith("/pn ")) {
                args.Cancel = true;

                var target = Pool.Server.Users.Find(_ => _.Identity.Id == args.Event.Input.Split(' ')[1])?.InternalId ?? Pool.Server.Accounts.Find(_ => _.Identity.Id == args.Event.Input.Split(' ')[1])?.InternalId;

                if (target == null) {
                    args.Event.Sender.ToTarget().SendPackage(new Package(PackageType.Message, new MessagePackageContent(pluginMember.InternalId, pluginMember.Identity, "Der Benutzer konnte nicht gefunden werden.", DateTime.Now, "received", args.Event.Sender.ActiveChannel.InternalId)));
                } else {
                    var conversation = conversations.Find(_ => _.Users.Contains(args.Event.Sender.InternalId) && _.Users.Contains(target.Value));

                    if (conversation != null) {
                        args.Event.Sender.MoveToChannel(conversation.Channel, true);
                        return;
                    }

                    var channel = new Channel {
                        Attributes = new Dictionary<string, object> {
                            { "neo.channeltype", "conversation" }
                        },
                        Id = $"~conversation+{args.Event.Sender.InternalId}+{target.Value}",
                        Lifetime = Lifespan.Permanent,
                        Limit = 2,
                        Name = $"Konversation zwischen {args.Event.Sender.InternalId} und {target.Value}",
                        MemberIds = new List<Guid> {
                            args.Event.Sender.InternalId,
                            target.Value
                        },
                        Password = ""
                    };

                    this.CreateChannel(pluginMember, channel);
                    Pool.Server.DataProvider.Save();

                    conversations.Add(new Conversation(args.Event.Sender.InternalId, target.Value, channel));
                    Save();

                    // TODO: Send notification to target
                    args.Event.Sender.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(args.Event.Sender.InternalId)))));

                    var targetUser = Pool.Server.Users.Find(_ => _.InternalId.Equals(target.Value));
                    targetUser?.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(targetUser.InternalId)))));

                    args.Event.Sender.MoveToChannel(channel);
                }
            }

            if (args.Event.Sender.ActiveChannel.Attributes.ContainsKey("neo.channeltype") && args.Event.Sender.ActiveChannel.Attributes["neo.channeltype"].ToString() == "conversation") {
                var conversation = conversations.Find(_ => _.Channel.InternalId.Equals(args.Event.Sender.ActiveChannel.InternalId));
                
                args.Event.Sender.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(args.Event.Sender.InternalId)))));
                
                var targetUser = Pool.Server.Users.Find(_ => _.InternalId.Equals(conversation.Users.Find(u => !u.Equals(args.Event.Sender.InternalId))));
                if (targetUser != null) {
                    if (!targetUser.ActiveChannel.InternalId.Equals(conversation.Channel.InternalId)) {
                        targetUser.ToTarget().SendPackage(new Package(PackageType.Message, MessagePackageContent.GetReceivedMessage(args.Event.Sender.InternalId, args.Event.Sender.Identity, args.Event.Input, conversation.Channel.InternalId)));
                    }
                    targetUser.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(targetUser.InternalId)))));
                }
            }
        }

        [EventListener(EventType.Custom)]
        public override async Task OnCustom(CustomEventArgs args) {
            if (args.Name == $"{Namespace}.start") {
                Logger.Instance.Log(LogLevel.Debug, "Start event from: " + args.Sender + " (Content: " + args.Content[0] + ")");
            }
        }

        public override async Task OnDispose() {
            Save();

            Logger.Instance.Log(LogLevel.Info, "Conversation Street disposed.");
        }

        public override async Task OnInitialize(string storagePath) {
            this.databasePath = Path.Combine(storagePath, @"conversations.json");

            if (!Directory.Exists(storagePath)) {
                Directory.CreateDirectory(storagePath);
            }

            if (!File.Exists(databasePath)) {
                File.WriteAllText(databasePath, "[]");
            } else {
                conversations = JsonConvert.DeserializeObject<List<Conversation>>(File.ReadAllText(databasePath));
            }
        }

        [EventListener(EventType.Login)]
        public override async Task OnLogin(LoginEventArgs args) {
            args.User.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(args.User.InternalId)))));
        }

        [EventListener(EventType.ServerInitialized)]
        public override async Task OnServerInitialized(BaseServer server) {
            Logger.Instance.Log(LogLevel.Info, "Conversation Street initialized.");

            pluginMember = Authenticator.CreateVirtualMember(this);
            pluginMember.Identity = new Identity { Id = "conversation", Name = "Conversation Street" };
        }

        private void Save() {
            File.WriteAllText(databasePath, JsonConvert.SerializeObject(conversations, Formatting.Indented));
        }
    }
}