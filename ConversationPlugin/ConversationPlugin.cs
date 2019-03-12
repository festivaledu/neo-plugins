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
            Logger.Instance.Log(LogLevel.Debug, "Channel join by " + args.Event.Joiner.Identity.Id + " in " + args.Event.Element.Id);
            conversations.FindAll(_ => _.Users.Contains(args.Event.Joiner.InternalId)).ForEach(_ => _.Channel.ActiveMemberIds.Remove(args.Event.Joiner.InternalId));

            if (args.Event.Element.Attributes.ContainsKey("neo.origin") && args.Event.Element.Attributes["neo.origin"].ToString() == Namespace && args.Event.Element.Id.StartsWith("~conversation+")) {
                args.Cancel = true;

                var conversation = conversations.Find(_ => _.Channel.InternalId.Equals(args.Event.Element.InternalId));

                if (conversation == null) {
                    conversation = new Conversation(Guid.Parse(args.Event.Element.Id.Split('+')[1]), Guid.Parse(args.Event.Element.Id.Split('+')[2]), args.Event.Element);
                    conversations.Add(conversation);
                }

                if (conversation.Users.Contains(args.Event.Joiner.InternalId)) {
                    Logger.Instance.Log(LogLevel.Debug, "Conversation found, moving to " + conversation.Channel.Id);
                    args.Event.Joiner.MoveToChannel(conversation.Channel, true);
                } else {
                    Logger.Instance.Log(LogLevel.Error, $"{args.Event.Joiner.Identity.Name} (@{args.Event.Joiner.Identity.Id}) is not part of this conversation.");
                }
            }
        }

        [EventListener(EventType.BeforeInput)]
        public override async Task OnBeforeInput(Before<InputEventArgs> args) {
            Logger.Instance.Log(LogLevel.Debug, "Input by " + args.Event.Sender.Identity.Id + " in " + args.Event.Sender.ActiveChannel.Id);

            if (args.Event.Sender.ActiveChannel.Attributes.ContainsKey("neo.channeltype") && args.Event.Sender.ActiveChannel.Attributes["neo.channeltype"].ToString() == "conversation") {
                args.Cancel = true;

                var conversation = conversations.Find(_ => _.Channel.InternalId.Equals(args.Event.Sender.ActiveChannel.InternalId));
                var received = MessagePackageContent.GetReceivedMessage(args.Event.Sender.InternalId, args.Event.Sender.Identity, args.Event.Input, conversation.Channel.InternalId);
                var sent = MessagePackageContent.GetSentMessage(args.Event.Sender.InternalId, args.Event.Sender.Identity, args.Event.Input, conversation.Channel.InternalId);

                conversation.Channel.SaveMessage(received);

                args.Event.Sender.ToTarget().SendPackage(new Package(PackageType.Message, sent));
                args.Event.Sender.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(args.Event.Sender.InternalId)))));
                
                var targetUser = Pool.Server.Users.Find(_ => _.InternalId.Equals(conversation.Users.Find(u => !u.Equals(args.Event.Sender.InternalId))));
                if (targetUser != null) {
                    targetUser.ToTarget().SendPackage(new Package(PackageType.Message, received));
                    targetUser.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(targetUser.InternalId)))));
                }
            }
        }

        [EventListener(EventType.Custom)]
        public override async Task OnCustom(CustomEventArgs args) {
            if (args.Name == $"{Namespace}.start") {
                Logger.Instance.Log(LogLevel.Debug, "Start conversation with " + args.Content[0]);

                var sender = Pool.Server.Users.Find(_ => _.InternalId.Equals(args.Sender));
                var target = Pool.Server.Accounts.Find(_ => _.Identity.Id == args.Content[0].ToString())?.InternalId;

                if (target == null) {
                    //sender.ToTarget().SendPackage(new Package(PackageType.Message, new MessagePackageContent(pluginMember.InternalId, pluginMember.Identity, "Der Benutzer konnte nicht gefunden werden.", DateTime.Now, "received", sender.ActiveChannel.InternalId)));
                } else {
                    var conversation = conversations.Find(_ => _.Users.Contains(sender.InternalId) && _.Users.Contains(target.Value));

                    if (conversation != null) {
                        Logger.Instance.Log(LogLevel.Debug, "Conversation found, moving to " + conversation.Channel.Id + " with " + conversation.Channel.ActiveMemberIds.Count + " active members");
                        sender.MoveToChannel(conversation.Channel, true);
                        return;
                    }

                    var channel = new Channel {
                        Attributes = new Dictionary<string, object> {
                            { "neo.channeltype", "conversation" }
                        },
                        Id = $"~conversation+{sender.InternalId}+{target.Value}",
                        Lifetime = Lifespan.Permanent,
                        Limit = 2,
                        Name = $"Konversation zwischen {sender.InternalId} und {target.Value}",
                        MemberIds = new List<Guid> {
                            sender.InternalId,
                            target.Value
                        },
                        Password = ""
                    };

                    this.CreateChannel(pluginMember, channel);
                    Pool.Server.DataProvider.Save();

                    conversations.Add(new Conversation(sender.InternalId, target.Value, channel));
                    Save();
                    
                    sender.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(sender.InternalId)))));

                    var targetUser = Pool.Server.Users.Find(_ => _.InternalId.Equals(target.Value));
                    targetUser?.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(targetUser.InternalId)))));

                    sender.MoveToChannel(channel, true);
                }
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