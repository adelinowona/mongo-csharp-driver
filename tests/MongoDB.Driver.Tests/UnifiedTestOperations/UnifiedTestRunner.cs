﻿/* Copyright 2021-present MongoDB Inc.
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.TestHelpers.JsonDrivenTests;
using MongoDB.Driver.Core;
using MongoDB.Driver.Core.Logging;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.Core.TestHelpers.Logging;
using MongoDB.Driver.Core.TestHelpers.XunitExtensions;
using MongoDB.Driver.Tests.UnifiedTestOperations.Matchers;
using MongoDB.TestHelpers.XunitExtensions;
using Xunit.Sdk;

namespace MongoDB.Driver.Tests.UnifiedTestOperations
{
    public interface IEventsProcessor
    {
        void PostProcessEvents(List<object> events, string type);
    }

    public sealed class UnifiedTestRunner : IDisposable
    {
        private UnifiedEntityMap _entityMap;
        private readonly Dictionary<string, object> _additionalArgs;
        private readonly Dictionary<string, IEventFormatter> _eventFormatters;
        private readonly IEventsProcessor _eventsProcessor;
        private bool _runHasBeenCalled;
        private readonly ILogger<UnifiedTestRunner> _logger;
        private readonly Predicate<LogEntry> _loggingFilter;
        private readonly ILoggingService _loggingService;

        public UnifiedTestRunner(
            ILoggingService loggingService,
            Dictionary<string, object> additionalArgs = null,
            Dictionary<string, IEventFormatter> eventFormatters = null,
            Predicate<LogEntry> loggingFilter = null,
            IEventsProcessor eventsProcessor = null)
        {
            _additionalArgs = additionalArgs; // can be null
            _eventFormatters = eventFormatters; // can be null
            _eventsProcessor = eventsProcessor; // can be null
            _loggingFilter = loggingFilter; // can be null
            _loggingService = Ensure.IsNotNull(loggingService, nameof(loggingService));
            _logger = loggingService.LoggingSettings.CreateLogger<UnifiedTestRunner>();
        }

        // public properties
        public UnifiedEntityMap EntityMap => _entityMap;

        public void Run(JsonDrivenTestCase testCase)
        {
            _logger.LogDebug("Running {0}", testCase.Name);

            // Top-level fields
            var schemaVersion = testCase.Shared["schemaVersion"].AsString; // cannot be null
            var testSetRunOnRequirements = testCase.Shared.GetValue("runOnRequirements", null)?.AsBsonArray;
            var entities = testCase.Shared.GetValue("createEntities", null)?.AsBsonArray;
            var initialData = testCase.Shared.GetValue("initialData", null)?.AsBsonArray;
            // Test fields
            var runOnRequirements = testCase.Test.GetValue("runOnRequirements", null)?.AsBsonArray;
            var skipReason = testCase.Test.GetValue("skipReason", null)?.AsString;
            var operations = testCase.Test["operations"].AsBsonArray; // cannot be null
            var expectEvents = testCase.Test.GetValue("expectEvents", null)?.AsBsonArray;
            var expectedLogs = testCase.Test.GetValue("expectLogMessages", null)?.AsBsonArray;
            var outcome = testCase.Test.GetValue("outcome", null)?.AsBsonArray;
            var async = testCase.Test["async"].AsBoolean; // cannot be null

            Run(schemaVersion, testSetRunOnRequirements, entities, initialData, runOnRequirements, skipReason, operations, expectEvents, expectedLogs, outcome, async);
        }

        public void Run(
            string schemaVersion,
            BsonArray testSetRunOnRequirements,
            BsonArray entities,
            BsonArray initialData,
            BsonArray runOnRequirements,
            string skipReason,
            BsonArray operations,
            BsonArray expectedEvents,
            BsonArray expectedLogs,
            BsonArray outcome,
            bool async)
        {
            if (_runHasBeenCalled)
            {
                throw new InvalidOperationException("The test suite has already been run.");
            }
            _runHasBeenCalled = true;

            var schemaSemanticVersion = SemanticVersion.Parse(schemaVersion);
            if (schemaSemanticVersion < new SemanticVersion(1, 0, 0) ||
                schemaSemanticVersion > new SemanticVersion(1, 22, 0))
            {
                throw new FormatException($"Schema version '{schemaVersion}' is not supported.");
            }
            if (testSetRunOnRequirements != null)
            {
                RequireServer.Check().RunOn(testSetRunOnRequirements);
            }
            if (runOnRequirements != null)
            {
                RequireServer.Check().RunOn(runOnRequirements);
            }
            if (skipReason != null)
            {
                throw new SkipException($"Test skipped because '{skipReason}'.");
            }

            // should skip on KillOpenTransactions for Atlas Data Lake tests.
            // https://github.com/mongodb/specifications/blob/80f88d0af6e47407c03874512e0d9b73708edad5/source/atlas-data-lake-testing/tests/README.md?plain=1#L23
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ATLAS_DATA_LAKE_TESTS_ENABLED")))
            {
                KillOpenTransactions(DriverTestConfiguration.Client);
            }

            BsonDocument lastKnownClusterTime = AddInitialData(DriverTestConfiguration.Client, initialData);
            _entityMap = UnifiedEntityMap.Create(_eventFormatters, _loggingService.LoggingSettings, async, lastKnownClusterTime);
            _entityMap.AddRange(entities);

            foreach (var operation in operations)
            {
                var cancellationToken = CancellationToken.None;
                CreateAndRunOperation(operation.AsBsonDocument, async, cancellationToken);
            }

            if (expectedEvents != null)
            {
                AssertEvents(expectedEvents, _entityMap);
            }
            if (expectedLogs != null)
            {
                AssertLogs(expectedLogs, _entityMap);
            }
            if (outcome != null)
            {
                AssertOutcome(DriverTestConfiguration.Client, outcome);
            }
        }

        public void Dispose()
        {
            _logger.LogDebug("Disposing");

            try
            {
                KillOpenTransactions(DriverTestConfiguration.Client);
            }
            catch
            {
                // Ignored because Dispose shouldn't fail
            }

            _logger.LogDebug("Disposing entity map");

            _entityMap?.Dispose();

            _logger.LogDebug("Disposed");
        }

        // private methods
        private BsonDocument AddInitialData(IMongoClient client, BsonArray initialData)
        {
            if (initialData == null)
            {
                return null;
            }

            BsonDocument lastKnownClusterTime = null;
            foreach (var dataItem in initialData)
            {
                var collectionName = dataItem["collectionName"].AsString;
                var databaseName = dataItem["databaseName"].AsString;
                var documents = dataItem["documents"].AsBsonArray.Cast<BsonDocument>().ToList();

                var database = client.GetDatabase(databaseName).WithWriteConcern(WriteConcern.WMajority);

                _logger.LogDebug("Dropping {0}", collectionName);
                using var session = client.StartSession();
                database.DropCollection(session, collectionName);
                database.CreateCollection(session, collectionName);
                if (documents.Any())
                {
                    var collection = database.GetCollection<BsonDocument>(collectionName);
                    collection.InsertMany(session, documents);
                }

                lastKnownClusterTime = session.ClusterTime;
            }

            return lastKnownClusterTime;
        }

        private void AssertEvents(BsonArray eventItems, UnifiedEntityMap entityMap)
        {
            _logger.LogDebug("Asserting events");

            var unifiedEventMatcher = new UnifiedEventMatcher(new UnifiedValueMatcher(entityMap));
            foreach (var eventItem in eventItems.Cast<BsonDocument>())
            {
                var clientId = eventItem["client"].AsString;
                var ignoreExtraEvents = eventItem.GetValue("ignoreExtraEvents", false).AsBoolean;
                var eventCapturer = entityMap.EventCapturers[clientId];
                var eventType = eventItem.GetValue("eventType", defaultValue: "command").AsString;
                var actualEvents = UnifiedEventMatcher.FilterEventsBySetType(eventCapturer.Events, eventType);

                _eventsProcessor?.PostProcessEvents(actualEvents, eventType);

                unifiedEventMatcher.AssertEventsMatch(actualEvents, eventItem["events"].AsBsonArray, ignoreExtraEvents);
            }
        }

        private void AssertLogs(BsonArray expectedLogs, UnifiedEntityMap entityMap)
        {
            _logger?.LogDebug("Asserting logs");

            var actualLogs = _loggingService.Logs;

            var unifiedLogMatcher = new UnifiedLogMatcher(new UnifiedValueMatcher(entityMap));
            foreach (var logItem in expectedLogs.Cast<BsonDocument>())
            {
                var clientId = logItem["client"].AsString;
                var clusterId = entityMap.ClientIdToClusterId[clientId].Value;
                var logs = logItem.GetValue("messages", false).AsBsonArray;
                var loggingMessagesToIgnore = logItem.GetValue("ignoreMessages", null)?.AsBsonArray;
                var ignoreExtraLogs = logItem.GetValue("ignoreExtraMessages", false).AsBoolean;

                var actualLogsFiltered = UnifiedLogHelper.FilterLogs(
                    actualLogs,
                    clientId,
                    clusterId,
                    entityMap.LoggingComponents,
                    loggingMessagesToIgnore,
                    _loggingFilter);

                unifiedLogMatcher.AssertLogsMatch(actualLogsFiltered, logs, ignoreExtraLogs);
            }
        }

        private OperationResult CreateAndRunOperation(BsonDocument operationDocument, bool async, CancellationToken cancellationToken)
        {
            var operation = CreateOperation(operationDocument, _entityMap);

            switch (operation)
            {
                case IUnifiedEntityTestOperation entityOperation:
                    var result = async
                        ? entityOperation.ExecuteAsync(cancellationToken).GetAwaiter().GetResult()
                        : entityOperation.Execute(cancellationToken);
                    AssertResult(result, operationDocument, _entityMap);
                    return result;
                case IUnifiedSpecialTestOperation specialOperation:
                    specialOperation.Execute();
                    return OperationResult.Empty();
                case IUnifiedOperationWithCreateAndRunOperationCallback operationWithCreateAndRunCallback:
                    var innerResult = async
                        ? operationWithCreateAndRunCallback.ExecuteAsync(CreateAndRunOperation, cancellationToken).GetAwaiter().GetResult()
                        : operationWithCreateAndRunCallback.Execute(CreateAndRunOperation, cancellationToken);
                    AssertResult(innerResult, operationDocument, _entityMap);
                    return innerResult;
                default:
                    throw new FormatException($"Unexpected operation type: '{operation.GetType()}'.");
            }
        }

        private void AssertOutcome(IMongoClient client, BsonArray outcome)
        {
            _logger.LogDebug("Asserting outcome");

            foreach (var outcomeItem in outcome)
            {
                var collectionName = outcomeItem["collectionName"].AsString;
                var databaseName = outcomeItem["databaseName"].AsString;
                var expectedData = outcomeItem["documents"].AsBsonArray.Cast<BsonDocument>().ToList();

                var findOptions = new FindOptions<BsonDocument> { Sort = "{ _id : 1 }" };
                var collection = client
                    .GetDatabase(databaseName)
                    .GetCollection<BsonDocument>(collectionName)
                    .WithReadPreference(ReadPreference.Primary);
                collection = collection.WithReadConcern(ReadConcern.Local);

                var actualData = collection
                    .FindSync(new EmptyFilterDefinition<BsonDocument>(), findOptions)
                    .ToList();

                actualData.Should().Equal(expectedData);
            }
        }

        private void AssertResult(OperationResult actualResult, BsonDocument operation, UnifiedEntityMap entityMap)
        {
            if (operation.GetValue("ignoreResultAndError", defaultValue: false).ToBoolean())
            {
                return;
            }

            if (operation.TryGetValue("expectResult", out var expectedResult))
            {
                actualResult.Exception.Should().BeNull();

                new UnifiedValueMatcher(entityMap).AssertValuesMatch(actualResult.Result, expectedResult);
            }
            if (operation.TryGetValue("expectError", out var expectedError))
            {
                actualResult.Exception.Should().NotBeNull();
                actualResult.Result.Should().BeNull();

                new UnifiedErrorMatcher(entityMap).AssertErrorsMatch(actualResult.Exception, expectedError.AsBsonDocument);
            }
            else
            {
                actualResult.Exception.Should().BeNull();
            }
            if (operation.TryGetValue("saveResultAsEntity", out var saveResultAsEntity))
            {
                if (actualResult.Result != null)
                {
                    entityMap.Resutls.Add(saveResultAsEntity.AsString, actualResult.Result);
                }
                else if (actualResult.ChangeStream != null)
                {
                    entityMap.ChangeStreams.Add(saveResultAsEntity.AsString, actualResult.ChangeStream);
                }
                else if (actualResult.Cursor != null)
                {
                    entityMap.Cursors.Add(saveResultAsEntity.AsString, actualResult.Cursor);
                }
                else
                {
                    throw new AssertionException($"Expected result to be present but none found to save with id: '{saveResultAsEntity.AsString}'.");
                }
            }
        }

        private IUnifiedTestOperation CreateOperation(BsonDocument operation, UnifiedEntityMap entityMap)
        {
            var factory = new UnifiedTestOperationFactory(entityMap, _additionalArgs);

            var operationName = operation["name"].AsString;
            var operationTarget = operation["object"].AsString;
            var operationArguments = operation.GetValue("arguments", null)?.AsBsonDocument;

            _logger.LogDebug("Created {0} operation", operationName);

            return factory.CreateOperation(operationName, operationTarget, operationArguments);
        }

        private void KillOpenTransactions(IMongoClient client)
        {
            var serverVersion = CoreTestConfiguration.ServerVersion;
            var command = new BsonDocument("killAllSessions", new BsonArray());
            var adminDatabase = client.GetDatabase(DatabaseNamespace.Admin.DatabaseName);

            try
            {
                adminDatabase.RunCommand<BsonDocument>(command);
            }
            catch (MongoCommandException ex) when (
                // SERVER-38335
                serverVersion < new SemanticVersion(4, 1, 9) && ex.Code == (int)ServerErrorCode.Interrupted ||
                // SERVER-54216
                ex.Code == (int)ServerErrorCode.Unauthorized)
            {
                // ignore errors
            }
        }
    }
}
