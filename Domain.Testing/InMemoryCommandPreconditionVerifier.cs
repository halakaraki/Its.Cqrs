// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Its.Domain.Testing
{
    public class InMemoryCommandPreconditionVerifier : ICommandPreconditionVerifier
    {
        private readonly ConcurrentDictionary<string, IEventStream> eventStreams;

        public InMemoryCommandPreconditionVerifier(ConcurrentDictionary<string, IEventStream> eventStreams = null)
        {
            this.eventStreams = eventStreams ?? new ConcurrentDictionary<string, IEventStream>();
        }

        public async Task<bool> IsPreconditionSatisfied(IScheduledCommand scheduledCommand)
        {
            if (scheduledCommand == null)
            {
                throw new ArgumentNullException("scheduledCommand");
            }

            if (scheduledCommand.DeliveryPrecondition == null)
            {
                return true;
            }

            var aggregateId = scheduledCommand.DeliveryPrecondition.AggregateId.ToString();
            var etag = scheduledCommand.DeliveryPrecondition.ETag;

            return eventStreams.Values.Any(v =>
            {
                var eventsForAggregate = v.All(aggregateId).Result;

                return eventsForAggregate.Any(e => e.ETag == etag);
            });
        }
    }
}