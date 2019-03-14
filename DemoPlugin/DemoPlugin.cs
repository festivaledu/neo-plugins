using System;
using Neo.Core.Authentication;
using Neo.Core.Communication;
using Neo.Core.Communication.Packages;
using Neo.Core.Cryptography;
using Neo.Core.Extensibility;
using Neo.Core.Extensibility.Events;
using Neo.Core.Shared;

namespace DemoPlugin
{
    public class DemoPlugin : Plugin
    {
        public override string Namespace => "ml.festival.demo";

        private Member pluginMember;

        [EventListener(EventType.BeforeInput)]
        public override void OnBeforeInput(Before<InputEventArgs> args) {
            if (args.Event.Input.StartsWith("/sha ")) {
                args.Cancel = true;

                args.Event.Sender.ToTarget().SendPackage(new Package(PackageType.Message, MessagePackageContent.GetReceivedMessage(InternalId, pluginMember.Identity, Convert.ToBase64String(NeoCryptoProvider.Instance.Sha512ComputeHash(args.Event.Input.Substring(5))), args.Event.Sender.ActiveChannel.InternalId)));
            }
        }
        
        public override void OnInitialize(string storagePath) {
            pluginMember = Authenticator.CreateVirtualMember(this);
            pluginMember.Identity = new Identity {
                Id = "demo",
                Name = "Demo Plugin"
            };
        }
    }
}
