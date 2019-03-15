using System;
using System.Collections.Generic;
using Neo.Core.Shared;

namespace ConversationPlugin
{
    public class Conversation
    {
        public List<Guid> Users { get; set; } = new List<Guid>();
        public Channel Channel { get; set; }


        public Conversation() { }

        public Conversation(Guid from, Guid to, Channel channel) {
            this.Users.Add(from);
            this.Users.Add(to);

            this.Channel = channel;
        }
    }
}
