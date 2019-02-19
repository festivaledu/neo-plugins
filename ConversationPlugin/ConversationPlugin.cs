using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Neo.Core.Authentication;
using Neo.Core.Communication;
using Neo.Core.Extensibility;
using Neo.Core.Extensibility.Events;
using Neo.Core.Management;
using Neo.Core.Networking;
using Neo.Core.Shared;

namespace ConversationPlugin
{
    public class ConversationPlugin : Plugin
    {
        public override string Namespace => "ml.festival.msg";

        private Member pluginMember;

        [EventListener(EventType.ServerInitialized)]
        public override async Task OnServerInitialized(BaseServer server) {
            pluginMember = Authenticator.CreateVirtualMember(this);
            pluginMember.Identity = new Identity {
                Id = "conversation",
                Name = "Conversation Plugin"
            };
            
            Logger.Instance.Log(LogLevel.Fatal, "Josef, du schaffst das vielleicht...", true);
        }

        [EventListener(EventType.BeforeInput)]
        public override async Task OnBeforeInput(Before<InputEventArgs> args) {
            if (args.Event.Input.StartsWith("/pn ")) {
                args.Cancel = true;

                var userId = args.Event.Input.Split(" ")[1];
                var user = Pool.Server.Users.Find(u => u.Identity.Id == userId);

                if (user == null) {
                    Pool.Server.SendPackageTo(new Target(args.Event.Sender), new Package(PackageType.Message, new {
                        identity = pluginMember.Identity,
                        message = userId + " wurde nicht gefunden.",
                        timestamp = DateTime.Now,
                        messageType = "received"
                    }));
                    return;
                }

                var channel = new Channel {
                    Attributes = new Dictionary<string, object> {
                        { "conversation", true }
                    },
                    Id = $"pn{args.Event.Sender.InternalId}-{user.InternalId}",
                    Lifetime = Lifespan.Permanent,
                    Limit = 2,
                    Name = "Konversation zwischen " + args.Event.Sender.Identity.Name + " und " + user.Identity.Name,
                    VisibleToUserIds = new List<Guid> {
                        args.Event.Sender.InternalId,
                        user.InternalId
                    }
                };

                pluginMember.CreateChannel(channel);

                args.Event.Sender.MoveToChannel(channel);
                user.JoinChannel(channel);

                Pool.Server.SendPackageTo(new Target(user), new Package(PackageType.Message, new {
                    identity = pluginMember.Identity,
                    message = args.Event.Sender.Identity.Name + " hat eine Konversation mit dir angefangen.",
                    timestamp = DateTime.Now,
                    messageType = "received"
                }));
            }
        }
    }
}
