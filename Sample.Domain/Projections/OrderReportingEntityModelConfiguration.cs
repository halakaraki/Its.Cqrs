// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Data.Entity.ModelConfiguration;
using System.Data.Entity.ModelConfiguration.Configuration;
using Microsoft.Its.Domain.Sql;

namespace Sample.Domain.Projections
{
    public class OrderReportingEntityModelConfiguration :
        IEntityModelConfiguration
    {
        public void ConfigureModel(ConfigurationRegistrar configurationRegistrar)
        {
            configurationRegistrar.Add(new OrderReportingEntityTypeConfig());
        }

        public class OrderReportingEntityTypeConfig : EntityTypeConfiguration<OrderTally>
        {
            public OrderReportingEntityTypeConfig()
            {
                HasKey(rp => rp.Status);
            }
        }
    }
}
