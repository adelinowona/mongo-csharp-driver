/* Copyright 2019-present MongoDB Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver.Core;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using MongoDB.Driver.Core.TestHelpers.Logging;
using MongoDB.TestHelpers.XunitExtensions;
using Xunit;
using Xunit.Abstractions;

namespace MongoDB.Driver.Tests
{
    [Trait("Category", "Integration")]
    public class ReadPreferenceOnStandaloneTests : LoggableTestClass
    {
        public ReadPreferenceOnStandaloneTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [ParameterAttributeData]
        public void ReadPreference_should_not_be_sent_to_standalone_server(
            [Values(false, true)] bool async)
        {
            var eventCapturer = new EventCapturer().Capture<CommandStartedEvent>(e =>
                e.CommandName.Equals("find") || e.CommandName.Equals("$query"));
            using (var client = CreateMongoClient(eventCapturer, ReadPreference.PrimaryPreferred))
            {
                var database = client.GetDatabase(DriverTestConfiguration.DatabaseNamespace.DatabaseName);
                var collection = database.GetCollection<BsonDocument>(DriverTestConfiguration.CollectionNamespace.CollectionName);

                if (async)
                {
                    var _ = collection.FindAsync("{ x : 2 }").GetAwaiter().GetResult();
                }
                else
                {
                    var _ = collection.FindSync("{ x : 2 }");
                }

                CommandStartedEvent sentCommand = ((CommandStartedEvent)eventCapturer.Events[0]);
                SpinWait.SpinUntil(() => client.Cluster.Description.Servers.Any(s => s.State == ServerState.Connected), TimeSpan.FromSeconds(5)).Should().BeTrue();

                var clusterType = client.Cluster.Description.Type;

                var expectedContainsReadPreference = clusterType != ClusterType.Standalone;
                var readPreferenceFieldName = sentCommand.Command.Contains("$readPreference")
                    ? "$readPreference"
                    : "readPreference";

                sentCommand.Command.Contains(readPreferenceFieldName).Should().Be(expectedContainsReadPreference);
            }
        }

        // private methods
        private IMongoClient CreateMongoClient(EventCapturer eventCapturer, ReadPreference readPreference) =>
            DriverTestConfiguration.CreateMongoClient((MongoClientSettings settings) =>
            {
                settings.ClusterConfigurator = c => c.Subscribe(eventCapturer);
                settings.ReadPreference = readPreference;
                settings.LoggingSettings = LoggingSettings;
            });
    }
}
