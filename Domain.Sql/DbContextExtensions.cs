// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Data;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Its.Domain.Serialization;

namespace Microsoft.Its.Domain.Sql
{
    public static class DbContextExtensions
    {
        public static void Unique<TProjection>(
            this DbContext context,
            Expression<Func<TProjection, object>> member,
            string schema = "dbo") 
            where TProjection : class
        {
            context.Database.ExecuteSqlCommand(AddUniqueIndex(context, member, schema));
        }

        internal static string AddUniqueIndex<TProjection>(
            this DbContext context, 
            Expression<Func<TProjection, object>> member,
            string schema = "dbo")
            where TProjection : class
        {
            var tableName = context.TableNameFor<TProjection>();
            return string.Format("CREATE UNIQUE INDEX IX_{0}_{1} ON {2}.{0} ({1})",
                                 tableName,
                                 member.MemberName(),
                                 schema);
        }

        public static void Unique<TProjection>(
            this DbContext context,
            Expression<Func<TProjection, object>> member1,
            Expression<Func<TProjection, object>> member2,
            string schema = "dbo") where TProjection : class
        {
            context.Database.ExecuteSqlCommand(
                string.Format("CREATE UNIQUE INDEX IX_{0}_{1}_{2} ON {3}.{0} ({1}, {2})",
                              context.TableNameFor<TProjection>(),
                              member1.MemberName(),
                              member2.MemberName(),
                              schema));
        }

        public static void SeedFromFile(this EventStoreDbContext context, FileInfo file)
        {
            using (var stream = file.OpenRead())
            using (var reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                var events = Serializer.FromJsonToEvents(json).ToArray();

                foreach (var e in events)
                {
                    context.Events.Add(e.ToStorableEvent());

                    // it's necessary to save at every event to preserve ordering, otherwise EF will reorder them
                    context.SaveChanges();
                }
            }
        }

        private static string TableNameFor<T>(this DbContext context) where T : class
        {
            var objectContext = ((IObjectContextAdapter) context).ObjectContext;

            var sql = objectContext.CreateObjectSet<T>().ToTraceString();

            var match = new Regex(@"FROM ([\[\]a-z0-9]*\.)?(?<table>.*) AS", RegexOptions.IgnoreCase)
                .Match(sql);

            var matched = match.Groups["table"].Value;

            return Regex.Replace(matched, @"[\[\]\.]", "");
        }

        internal static DbConnection OpenConnection(this DbContext context)
        {
            var connection = context.Database.Connection;
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }
            return connection;
        }

        public static bool IsAzureDatabase(this DbContext context)
        {
            return context.Database.Connection.ConnectionString.Contains("database.windows.net");
        }

        /// <summary>
        /// Creates an Azure database.
        /// </summary>
        /// <param name="context">The DbContext</param>
        /// <param name="dbSizeInGB">Size of database in GB</param>
        /// <param name="edition">Edition of database</param>
        /// <param name="serviceObjective">Service objective of database</param>
        /// <exception cref="System.ArgumentException">Not Azure database based on ConnectionString</exception>
        /// <remarks>
        /// See https://msdn.microsoft.com/en-us/library/dn268335.aspx for more info.
        /// MAXSIZE = ( [ 100 MB | 500 MB ] | [ { 1 | 5 | 10 | 20 | 30 � 150�500 } GB  ] )
        /// | EDITION = { 'web' | 'business' | 'basic' | 'standard' | 'premium' }
        /// | SERVICE_OBJECTIVE = { 'shared' | 'basic' | 'S0' | 'S1' | 'S2' | 'P1' | 'P2' | 'P3' }
        /// </remarks>
        public static void CreateAzureDatabase(this DbContext context, int dbSizeInGB, string edition, string serviceObjective)
        {
            if (!context.IsAzureDatabase())
            {
                throw new ArgumentException("Not Azure database based on ConnectionString");
            }

            var connstrBldr = new SqlConnectionStringBuilder(context.Database.Connection.ConnectionString)
            {
                InitialCatalog = "master"
            };

            var databaseName = context.Database.Connection.Database;
            var dbCreationCmd = string.Format("CREATE DATABASE [{0}] (MAXSIZE={1}GB, EDITION='{2}', SERVICE_OBJECTIVE='{3}')",
                    databaseName, dbSizeInGB, edition, serviceObjective);

            // With Azure SQL db V12, database creation TSQL became a sync process. 
            // So we need a 10 minutes command timeout
            ExecuteNonQuery(connstrBldr.ConnectionString, dbCreationCmd, commandTimeout: 600);
            context.WaitUntilDatabaseCreated();
        }

        public static void CreateReadonlyUser(this DbContext context, DbReadonlyUser readonlyUser)
        {
            var createUserCmd = string.Format("CREATE USER [{0}] FOR LOGIN [{1}]", 
                readonlyUser.UserName, readonlyUser.LoginName);
            ExecuteNonQuery(context.Database.Connection.ConnectionString, createUserCmd);

            var addRoleToUserCmd = string.Format("EXEC sp_addrolemember N'db_datareader', N'{0}'",
                readonlyUser.UserName);
            ExecuteNonQuery(context.Database.Connection.ConnectionString, addRoleToUserCmd);
        }

        private static void WaitUntilDatabaseCreated(this DbContext context)
        {
            // wait up to 60 seconds
            var sleepInSeconds = 2;
            var retryCount = 30;
            Exception exception = null;

            while (true)
            {
                if (retryCount <= 0)
                {
                    throw exception ?? new TimeoutException("Database is not ONLINE after 60 seconds");
                }

                try
                {
                    if (context.Database.Exists())
                    {
                        context.Database.Initialize(force: true);
                        break;
                    }
                }
                catch (Exception e)
                {
                    exception = e;
                }

                retryCount--;
                Thread.Sleep(TimeSpan.FromSeconds(sleepInSeconds));
            }
        }

        private static void ExecuteNonQuery(string connString, string commandText, int commandTimeout = 60)
        {
            using (var conn = new SqlConnection(connString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandTimeout = commandTimeout;
                cmd.CommandText = commandText;
                cmd.ExecuteNonQuery();
            }
        }
    }
}
