﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Matrix.SynapseInterop.Database.SynapseModels
{
    [Table("device_federation_outbox")]
    public class DeviceFederationOutbox
    {
        [Column("destination")]
        public string Destination { get; set; }

        [Column("stream_id")]
        public int StreamId { get; set; }

        [Column("queued_ts")]
        public long QueuedTs { get; set; }

        [Column("messages_json")]
        public string MessagesJson { get; set; }
    }
}
