﻿// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Reflection;

namespace Microsoft.Its.Domain
{
    public abstract class CommandSchedulerPipelineInitializer : ICommandSchedulerPipelineInitializer
    {
        private readonly MethodInfo initializeFor;

        protected CommandSchedulerPipelineInitializer()
        {
            initializeFor = GetType().GetMethod("InitializeFor", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public void Initialize(Configuration configuration)
        {
            configuration.Properties.GetOrAdd(GetKeyIndicatingInitialized(), _ =>
            {
                AggregateType.KnownTypes.ForEach(aggregateType =>
                {
                    initializeFor.MakeGenericMethod(aggregateType).Invoke(this, new[] { configuration });
                });
                return true;
            });
        }

        protected internal virtual string GetKeyIndicatingInitialized()
        {
            return GetType().ToString();
        }

        protected abstract void InitializeFor<TAggregate>(Configuration configuration)
            where TAggregate : class, IEventSourced;
    }
}