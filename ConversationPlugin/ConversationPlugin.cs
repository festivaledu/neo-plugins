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

        private readonly string[] launchTexts = {
            "It's a segment we are calling Conversation Street.",
            "It's time for us to take a gentle stroll down Conversation Street.",
            "It's time for us to check our mirrors and make a smooth left into Conversation Street.",
            "It's time for us to make a gentle left into Conversation Street.",
            "It is now time for us to engage reverse and park neatly in a marked space on Conversation Street.",
            "It's time to drop it a cog and hook a left into Conversation Street.",
            "Let's do that by popping some loose change in the ticket machine so we can park awhile on Conversation Street.",
            "It's time now for us to enjoy a gentle stroll along the sunlit sidewalks of Conversation Street.",
            "It's time now for us to take a gentle cruise down the velvety smoothness of Conversation Street.",
            "It is time to set the sat-nav for destination ‚chat‘, as we head down Conversation Street.",
            "It is now time for us to visit the headquarters of Chat & Co, who are, of course, based on Conversation Street.",
            "It's time for us to take a stroll down the smooth sidewalks of Conversation Street.",
            "It is time for us to lean on the lamppost of chat, in Conversation Street.",
            "It is now time for us to peer down a manhole of chat on Conversation Street.",
            "It's time for us to plant a sapling of chat on Conversation Street.",
            "It is time for us to pop into the post office of chat on Conversation Street.",
            "It's time to slide across an icy puddle of chat on Conversation Street.",
            "It is now time for us to plant some daffodils of opinion on the roundabout of chat at the end of Conversation Street.",
            "It is time to ring the doorbell of debate on the house of chat, located on Conversation Street.",
            "It's time to order some doughnuts of debate from the Chat Café, on Conversation Street.",
            "It’s time to step in a dog turd of chat on Conversation Street.",
            "It is time to brim the tank of chat from the petrol station of debate on the corner of Conversation Street.",
            "It is time for us to scrump an apple of chat from the orchid of intercourse which is on Conversation Street.",
            "It is time for us to splash in some puddles of chat left by the drizzle of debate that falls on Conversation Street.",
            "It’s time to say hello to the old lady of debate who sits in the bus shelter of chat on Conversation Street.",
            "It is time to buy a four-pack of chat from the off-license of debate on Conversation Street."
        };

        [EventListener(EventType.BeforeChannelJoin)]
        public override async Task OnBeforeChannelJoin(Before<JoinElementEventArgs<Channel>> args) {
            conversations.FindAll(_ => _.Users.Contains(args.Event.Joiner.InternalId)).ForEach(_ => _.Channel.ActiveMemberIds.Remove(args.Event.Joiner.InternalId));

            if (args.Event.Element.Attributes.ContainsKey("neo.origin") && args.Event.Element.Attributes["neo.origin"].ToString() == Namespace && args.Event.Element.Id.StartsWith("~conversation+")) {
                args.Cancel = true;

                var conversation = conversations.Find(_ => _.Channel.InternalId.Equals(args.Event.Element.InternalId));

                if (conversation == null) {
                    conversation = new Conversation(Guid.Parse(args.Event.Element.Id.Split('+')[1]), Guid.Parse(args.Event.Element.Id.Split('+')[2]), args.Event.Element);
                    conversations.Add(conversation);
                }

                if (conversation.Users.Contains(args.Event.Joiner.InternalId)) {
                    args.Event.Joiner.MoveToChannel(conversation.Channel, true);
                }
            }
        }

        [EventListener(EventType.BeforeInput)]
        public override async Task OnBeforeInput(Before<InputEventArgs> args) {
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
                var sender = Pool.Server.Users.Find(_ => _.InternalId.Equals(args.Sender));
                var target = Pool.Server.Accounts.Find(_ => _.Identity.Id == args.Content[0].ToString())?.InternalId;

                if (target == null) {
                    //sender.ToTarget().SendPackage(new Package(PackageType.Message, new MessagePackageContent(pluginMember.InternalId, pluginMember.Identity, "Der Benutzer konnte nicht gefunden werden.", DateTime.Now, "received", sender.ActiveChannel.InternalId)));
                } else {
                    var conversation = conversations.Find(_ => _.Users.Contains(sender.InternalId) && _.Users.Contains(target.Value));

                    if (conversation != null) {
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
            } else if (args.Name == $"{Namespace}.stop") {
                var sender = Pool.Server.Users.Find(_ => _.InternalId.Equals(args.Sender));
                var conversation = conversations.Find(_ => _.Channel.InternalId.ToString() == args.Content[0].ToString());

                if (conversation == null) {
                    return;
                }

                conversations.Remove(conversation);
                Pool.Server.Channels.RemoveAll(_ => _.InternalId.Equals(conversation.Channel.InternalId));
                Pool.Server.DataProvider.Save();

                var targetUser = Pool.Server.Users.Find(user => user.InternalId.Equals(conversation.Users.Find(_ => !_.Equals(args.Sender))));

                sender.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(sender.InternalId)))));
                targetUser?.ToTarget().SendPackage(new Package(PackageType.CustomEvent, new CustomEventArgs($"{Namespace}.update", InternalId, conversations.FindAll(_ => _.Users.Contains(targetUser.InternalId)))));

                sender.MoveToChannel(ChannelManager.GetMainChannel());
                targetUser?.MoveToChannel(ChannelManager.GetMainChannel());

                Save();
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
            Logger.Instance.Log(LogLevel.Info, launchTexts[new Random().Next(launchTexts.Length)]);

            pluginMember = Authenticator.CreateVirtualMember(this);
            pluginMember.Identity = new Identity { Id = "conversation", Name = "Conversation Street" };
        }

        private void Save() {
            File.WriteAllText(databasePath, JsonConvert.SerializeObject(conversations, Formatting.Indented));
        }
    }
}