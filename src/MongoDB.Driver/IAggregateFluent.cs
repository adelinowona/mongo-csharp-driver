/* Copyright 2010-present MongoDB Inc.
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
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver.Search;

namespace MongoDB.Driver
{
    /// <summary>
    /// Fluent interface for aggregate.
    /// </summary>
    /// <remarks>
    /// This interface is not guaranteed to remain stable. Implementors should use
    /// <see cref="AggregateFluentBase{TResult}" />.
    /// </remarks>
    /// <typeparam name="TResult">The type of the result of the pipeline.</typeparam>
    public interface IAggregateFluent<TResult> : IAsyncCursorSource<TResult>
    {
        /// <summary>
        /// Gets the database.
        /// </summary>
        IMongoDatabase Database { get; }

        /// <summary>
        /// Gets the options.
        /// </summary>
        AggregateOptions Options { get; }

        /// <summary>
        /// Gets the stages.
        /// </summary>
        IList<IPipelineStageDefinition> Stages { get; }

        /// <summary>
        /// Appends the stage to the pipeline.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the result of the stage.</typeparam>
        /// <param name="stage">The stage.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> AppendStage<TNewResult>(PipelineStageDefinition<TResult, TNewResult> stage);

        /// <summary>
        /// Changes the result type of the pipeline.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="newResultSerializer">The new result serializer.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> As<TNewResult>(IBsonSerializer<TNewResult> newResultSerializer = null);

        /// <summary>
        /// Appends a $bucket stage to the pipeline.
        /// </summary>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <param name="groupBy">The expression providing the value to group by.</param>
        /// <param name="boundaries">The bucket boundaries.</param>
        /// <param name="options">The options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<AggregateBucketResult<TValue>> Bucket<TValue>(
            AggregateExpressionDefinition<TResult, TValue> groupBy,
            IEnumerable<TValue> boundaries,
            AggregateBucketOptions<TValue> options = null);

        /// <summary>
        /// Appends a $bucket stage to the pipeline with a custom projection.
        /// </summary>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="groupBy">The expression providing the value to group by.</param>
        /// <param name="boundaries">The bucket boundaries.</param>
        /// <param name="output">The output projection.</param>
        /// <param name="options">The options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> Bucket<TValue, TNewResult>(
            AggregateExpressionDefinition<TResult, TValue> groupBy,
            IEnumerable<TValue> boundaries,
            ProjectionDefinition<TResult, TNewResult> output,
            AggregateBucketOptions<TValue> options = null);

        /// <summary>
        /// Appends a $bucketAuto stage to the pipeline.
        /// </summary>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <param name="groupBy">The expression providing the value to group by.</param>
        /// <param name="buckets">The number of buckets.</param>
        /// <param name="options">The options (optional).</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<AggregateBucketAutoResult<TValue>> BucketAuto<TValue>(
            AggregateExpressionDefinition<TResult, TValue> groupBy,
            int buckets,
            AggregateBucketAutoOptions options = null);

        /// <summary>
        /// Appends a $bucketAuto stage to the pipeline with a custom projection.
        /// </summary>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="groupBy">The expression providing the value to group by.</param>
        /// <param name="buckets">The number of buckets.</param>
        /// <param name="output">The output projection.</param>
        /// <param name="options">The options (optional).</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> BucketAuto<TValue, TNewResult>(
            AggregateExpressionDefinition<TResult, TValue> groupBy,
            int buckets,
            ProjectionDefinition<TResult, TNewResult> output,
            AggregateBucketAutoOptions options = null);

        /// <summary>
        /// Appends a $changeStream stage to the pipeline.
        /// Normally you would prefer to use the Watch method of <see cref="IMongoCollection{TDocument}" />.
        /// Only use this method if subsequent stages project away the resume token (the _id)
        /// or you don't want the resulting cursor to automatically resume.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<ChangeStreamDocument<TResult>> ChangeStream(ChangeStreamStageOptions options = null);

        /// <summary>
        /// Appends a count stage to the pipeline.
        /// </summary>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<AggregateCountResult> Count();

        /// <summary>
        /// Appends a $densify stage to the pipeline.
        /// </summary>
        /// <param name="field">The field.</param>
        /// <param name="range">The range.</param>
        /// <param name="partitionByFields">The fields to partition by.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> Densify(
            FieldDefinition<TResult> field,
            DensifyRange range,
            IEnumerable<FieldDefinition<TResult>> partitionByFields = null);

        /// <summary>
        /// Appends a $densify stage to the pipeline.
        /// </summary>
        /// <param name="field">The field.</param>
        /// <param name="range">The range.</param>
        /// <param name="partitionByFields">The fields to partition by.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> Densify(
            FieldDefinition<TResult> field,
            DensifyRange range,
            params FieldDefinition<TResult>[] partitionByFields);

        /// <summary>
        /// Appends a $facet stage to the pipeline.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="facets">The facets.</param>
        /// <param name="options">The options.</param>
        /// <returns>
        /// The fluent aggregate interface.
        /// </returns>
        IAggregateFluent<TNewResult> Facet<TNewResult>(
            IEnumerable<AggregateFacet<TResult>> facets,
            AggregateFacetOptions<TNewResult> options = null);

        /// <summary>
        /// Appends a $graphLookup stage to the pipeline.
        /// </summary>
        /// <typeparam name="TFrom">The type of the from documents.</typeparam>
        /// <typeparam name="TConnectFrom">The type of the connect from field (must be either TConnectTo or a type that implements IEnumerable{TConnectTo}).</typeparam>
        /// <typeparam name="TConnectTo">The type of the connect to field.</typeparam>
        /// <typeparam name="TStartWith">The type of the start with expression (must be either TConnectTo or a type that implements IEnumerable{TConnectTo}).</typeparam>
        /// <typeparam name="TAsElement">The type of the as field elements.</typeparam>
        /// <typeparam name="TAs">The type of the as field.</typeparam>
        /// <typeparam name="TNewResult">The type of the new result (must be same as TResult with an additional as field).</typeparam>
        /// <param name="from">The from collection.</param>
        /// <param name="connectFromField">The connect from field.</param>
        /// <param name="connectToField">The connect to field.</param>
        /// <param name="startWith">The start with value.</param>
        /// <param name="as">The as field.</param>
        /// <param name="depthField">The depth field.</param>
        /// <param name="options">The options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> GraphLookup<TFrom, TConnectFrom, TConnectTo, TStartWith, TAsElement, TAs, TNewResult>(
            IMongoCollection<TFrom> from,
            FieldDefinition<TFrom, TConnectFrom> connectFromField,
            FieldDefinition<TFrom, TConnectTo> connectToField,
            AggregateExpressionDefinition<TResult, TStartWith> startWith,
            FieldDefinition<TNewResult, TAs> @as,
            FieldDefinition<TAsElement, int> depthField,
            AggregateGraphLookupOptions<TFrom, TAsElement, TNewResult> options = null)
                where TAs : IEnumerable<TAsElement>;

        /// <summary>
        /// Appends a group stage to the pipeline.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the result of the stage.</typeparam>
        /// <param name="group">The group projection.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> Group<TNewResult>(ProjectionDefinition<TResult, TNewResult> group);

        /// <summary>
        /// Appends a limit stage to the pipeline.
        /// </summary>
        /// <param name="limit">The limit.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> Limit(long limit);

        /// <summary>
        /// Appends a lookup stage to the pipeline.
        /// </summary>
        /// <typeparam name="TForeignDocument">The type of the foreign document.</typeparam>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="foreignCollectionName">Name of the other collection.</param>
        /// <param name="localField">The local field.</param>
        /// <param name="foreignField">The foreign field.</param>
        /// <param name="as">The field in <typeparamref name="TNewResult" /> to place the foreign results.</param>
        /// <param name="options">The options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> Lookup<TForeignDocument, TNewResult>(string foreignCollectionName, FieldDefinition<TResult> localField, FieldDefinition<TForeignDocument> foreignField, FieldDefinition<TNewResult> @as, AggregateLookupOptions<TForeignDocument, TNewResult> options = null);

        /// <summary>
        /// Appends a lookup stage to the pipeline.
        /// </summary>
        /// <typeparam name="TForeignDocument">The type of the foreign collection documents.</typeparam>
        /// <typeparam name="TAsElement">The type of the as field elements.</typeparam>
        /// <typeparam name="TAs">The type of the as field.</typeparam>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="foreignCollection">The foreign collection.</param>
        /// <param name="let">The "let" definition.</param>
        /// <param name="lookupPipeline">The lookup pipeline.</param>
        /// <param name="as">The as field in <typeparamref name="TNewResult" /> in which to place the results of the lookup pipeline.</param>
        /// <param name="options">The options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> Lookup<TForeignDocument, TAsElement, TAs, TNewResult>(
            IMongoCollection<TForeignDocument> foreignCollection,
            BsonDocument let,
            PipelineDefinition<TForeignDocument, TAsElement> lookupPipeline,
            FieldDefinition<TNewResult, TAs> @as,
            AggregateLookupOptions<TForeignDocument, TNewResult> options = null)
            where TAs : IEnumerable<TAsElement>;

        /// <summary>
        /// Appends a match stage to the pipeline.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> Match(FilterDefinition<TResult> filter);

        /// <summary>
        /// Appends a merge stage to the pipeline and executes it, and then returns a cursor to read the contents of the output collection.
        /// </summary>
        /// <typeparam name="TOutput">The type of output documents.</typeparam>
        /// <param name="outputCollection">The output collection.</param>
        /// <param name="mergeOptions">The merge options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A cursor.</returns>
        IAsyncCursor<TOutput> Merge<TOutput>(IMongoCollection<TOutput> outputCollection, MergeStageOptions<TOutput> mergeOptions = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Appends a merge stage to the pipeline and executes it, and then returns a cursor to read the contents of the output collection.
        /// </summary>
        /// <typeparam name="TOutput">The type of output documents.</typeparam>
        /// <param name="outputCollection">The output collection.</param>
        /// <param name="mergeOptions">The merge options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A cursor.</returns>
        Task<IAsyncCursor<TOutput>> MergeAsync<TOutput>(IMongoCollection<TOutput> outputCollection, MergeStageOptions<TOutput> mergeOptions = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Appends a match stage to the pipeline that matches derived documents and changes the result type to the derived type.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the derived documents.</typeparam>
        /// <param name="newResultSerializer">The new result serializer.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> OfType<TNewResult>(IBsonSerializer<TNewResult> newResultSerializer = null) where TNewResult : TResult;

        /// <summary>
        /// Appends an out stage to the pipeline and executes it, and then returns a cursor to read the contents of the output collection.
        /// </summary>
        /// <param name="outputCollection">The output collection.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A cursor.</returns>
        IAsyncCursor<TResult> Out(IMongoCollection<TResult> outputCollection, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Appends an out stage to the pipeline and executes it, and then returns a cursor to read the contents of the output collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A cursor.</returns>
        IAsyncCursor<TResult> Out(string collectionName, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Appends an out stage to the pipeline and executes it, and then returns a cursor to read the contents of the output collection.
        /// </summary>
        /// <param name="outputCollection">The output collection.</param>
        /// <param name="timeSeriesOptions">The time series options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A cursor.</returns>
        IAsyncCursor<TResult> Out(IMongoCollection<TResult> outputCollection, TimeSeriesOptions timeSeriesOptions, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Appends an out stage to the pipeline and executes it, and then returns a cursor to read the contents of the output collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection.</param>
        /// <param name="timeSeriesOptions">The time series options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A cursor.</returns>
        IAsyncCursor<TResult> Out(string collectionName, TimeSeriesOptions timeSeriesOptions, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Appends an out stage to the pipeline and executes it, and then returns a cursor to read the contents of the output collection.
        /// </summary>
        /// <param name="outputCollection">The output collection.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A Task whose result is a cursor.</returns>
        Task<IAsyncCursor<TResult>> OutAsync(IMongoCollection<TResult> outputCollection, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Appends an out stage to the pipeline and executes it, and then returns a cursor to read the contents of the output collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A Task whose result is a cursor.</returns>
        Task<IAsyncCursor<TResult>> OutAsync(string collectionName, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Appends an out stage to the pipeline and executes it, and then returns a cursor to read the contents of the output collection.
        /// </summary>
        /// <param name="outputCollection">The output collection.</param>
        /// <param name="timeSeriesOptions">The time series options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A Task whose result is a cursor.</returns>
        Task<IAsyncCursor<TResult>> OutAsync(IMongoCollection<TResult> outputCollection, TimeSeriesOptions timeSeriesOptions, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Appends an out stage to the pipeline and executes it, and then returns a cursor to read the contents of the output collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection.</param>
        /// <param name="timeSeriesOptions">The time series options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A Task whose result is a cursor.</returns>
        Task<IAsyncCursor<TResult>> OutAsync(string collectionName, TimeSeriesOptions timeSeriesOptions, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Appends a project stage to the pipeline.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the result of the stage.</typeparam>
        /// <param name="projection">The projection.</param>
        /// <returns>
        /// The fluent aggregate interface.
        /// </returns>
        IAggregateFluent<TNewResult> Project<TNewResult>(ProjectionDefinition<TResult, TNewResult> projection);

        /// <summary>
        /// Appends a $rankFusion stage to the pipeline.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="pipelines">The map of named pipelines whose results will be combined. The pipelines must operate on the same collection.</param>
        /// <param name="weights">The map of pipeline names to non-negative numerical weights determining result importance during combination. Default weight is 1 when unspecified.</param>
        /// <param name="options">The rankFusion options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> RankFusion<TNewResult>(
            Dictionary<string, PipelineDefinition<TResult, TNewResult>> pipelines,
            Dictionary<string, double> weights = null,
            RankFusionOptions<TNewResult> options = null);

        /// <summary>
        /// Appends a $rankFusion stage to the pipeline. Pipelines will be automatically named as "pipeline1", "pipeline2", etc.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="pipelines">The collection of pipelines whose results will be combined. The pipelines must operate on the same collection.</param>
        /// <param name="options">The rankFusion options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> RankFusion<TNewResult>(
            PipelineDefinition<TResult, TNewResult>[] pipelines,
            RankFusionOptions<TNewResult> options = null);

        /// <summary>
        /// Appends a $rankFusion stage to the pipeline. Pipelines will be automatically named as "pipeline1", "pipeline2", etc.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="pipelinesWithWeights">The collection of tuples containing (pipeline, weight) pairs. The pipelines must operate on the same collection.</param>
        /// <param name="options">The rankFusion options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> RankFusion<TNewResult>(
            (PipelineDefinition<TResult, TNewResult>, double?)[] pipelinesWithWeights,
            RankFusionOptions<TNewResult> options = null);

        /// <summary>
        /// Appends a $replaceRoot stage to the pipeline.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="newRoot">The new root.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> ReplaceRoot<TNewResult>(AggregateExpressionDefinition<TResult, TNewResult> newRoot);

        /// <summary>
        /// Appends a $replaceWith stage to the pipeline.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="newRoot">The new root.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> ReplaceWith<TNewResult>(AggregateExpressionDefinition<TResult, TNewResult> newRoot);

        /// <summary>
        /// Appends a sample stage to the pipeline.
        /// </summary>
        /// <param name="size">The sample size.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> Sample(long size);

        /// <summary>
        /// Appends a $set stage to the pipeline.
        /// </summary>
        /// <param name="fields">The fields to set.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> Set(SetFieldDefinitions<TResult> fields);

        /// <summary>
        /// Appends a $setWindowFields to the pipeline.
        /// </summary>
        /// <typeparam name="TWindowFields">The type of the added window fields.</typeparam>
        /// <param name="output">The window fields definition.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<BsonDocument> SetWindowFields<TWindowFields>(
            AggregateExpressionDefinition<ISetWindowFieldsPartition<TResult>, TWindowFields> output);

        //TODO If I add a parameter here, then this would be a binary breaking change
        /// <summary>
        /// Appends a $search stage to the pipeline.
        /// </summary>
        /// <param name="searchDefinition">The search definition.</param>
        /// <param name="highlight">The highlight options.</param>
        /// <param name="indexName">The index name.</param>
        /// <param name="count">The count options.</param>
        /// <param name="returnStoredSource">
        /// Flag that specifies whether to perform a full document lookup on the backend database
        /// or return only stored source fields directly from Atlas Search.
        /// </param>
        /// <param name="scoreDetails">
        /// Flag that specifies whether to return a detailed breakdown
        /// of the score for each document in the result.
        /// </param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> Search(
            SearchDefinition<TResult> searchDefinition,
            SearchHighlightOptions<TResult> highlight = null,
            string indexName = null,
            SearchCountOptions count = null,
            bool returnStoredSource = false,
            bool scoreDetails = false);

        /// <summary>
        /// Appends a $search stage to the pipeline.
        /// </summary>
        /// <param name="searchDefinition">The search definition.</param>
        /// <param name="searchOptions">The search options.</param>
        /// <returns>
        /// The fluent aggregate interface.
        /// </returns>
        IAggregateFluent<TResult> Search(
            SearchDefinition<TResult> searchDefinition,
            SearchOptions<TResult> searchOptions);

        /// <summary>
        /// Appends a $searchMeta stage to the pipeline.
        /// </summary>
        /// <param name="searchDefinition">The search definition.</param>
        /// <param name="indexName">The index name.</param>
        /// <param name="count">The count options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<SearchMetaResult> SearchMeta(
            SearchDefinition<TResult> searchDefinition,
            string indexName = null,
            SearchCountOptions count = null);

        /// <summary>
        /// Appends a $setWindowFields to the pipeline.
        /// </summary>
        /// <typeparam name="TPartitionBy">The type of the value to partition by.</typeparam>
        /// <typeparam name="TWindowFields">The type of the added window fields.</typeparam>
        /// <param name="partitionBy">The partitionBy definition.</param>
        /// <param name="output">The window fields definition.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<BsonDocument> SetWindowFields<TPartitionBy, TWindowFields>(
            AggregateExpressionDefinition<TResult, TPartitionBy> partitionBy,
            AggregateExpressionDefinition<ISetWindowFieldsPartition<TResult>, TWindowFields> output);

        /// <summary>
        /// Appends a $setWindowFields to the pipeline.
        /// </summary>
        /// <typeparam name="TPartitionBy">The type of the value to partition by.</typeparam>
        /// <typeparam name="TWindowFields">The type of the added window fields.</typeparam>
        /// <param name="partitionBy">The partitionBy definition.</param>
        /// <param name="sortBy">The sortBy definition.</param>
        /// <param name="output">The window fields definition.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<BsonDocument> SetWindowFields<TPartitionBy, TWindowFields>(
            AggregateExpressionDefinition<TResult, TPartitionBy> partitionBy,
            SortDefinition<TResult> sortBy,
            AggregateExpressionDefinition<ISetWindowFieldsPartition<TResult>, TWindowFields> output);

        /// <summary>
        /// Appends a skip stage to the pipeline.
        /// </summary>
        /// <param name="skip">The number of documents to skip.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> Skip(long skip);

        /// <summary>
        /// Appends a sort stage to the pipeline.
        /// </summary>
        /// <param name="sort">The sort specification.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> Sort(SortDefinition<TResult> sort);

        /// <summary>
        /// Appends a sortByCount stage to the pipeline.
        /// </summary>
        /// <typeparam name="TId">The type of the identifier.</typeparam>
        /// <param name="id">The identifier.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<AggregateSortByCountResult<TId>> SortByCount<TId>(AggregateExpressionDefinition<TResult, TId> id);

        /// <summary>
        /// Executes an aggregation pipeline that writes the results to a collection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        void ToCollection(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Executes an aggregation pipeline that writes the results to a collection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A Task.</returns>
        Task ToCollectionAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Appends an $unionWith stage to the pipeline.
        /// </summary>
        /// <typeparam name="TWith">The type of the with collection documents.</typeparam>
        /// <param name="withCollection">The with collection.</param>
        /// <param name="withPipeline">The with pipeline.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> UnionWith<TWith>(
            IMongoCollection<TWith> withCollection,
            PipelineDefinition<TWith, TResult> withPipeline = null);

        /// <summary>
        /// Appends an unwind stage to the pipeline.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the result of the stage.</typeparam>
        /// <param name="field">The field.</param>
        /// <param name="newResultSerializer">The new result serializer.</param>
        /// <returns>
        /// The fluent aggregate interface.
        /// </returns>
        [Obsolete("Use the Unwind overload which takes an options parameter.")]
        IAggregateFluent<TNewResult> Unwind<TNewResult>(FieldDefinition<TResult> field, IBsonSerializer<TNewResult> newResultSerializer);

        /// <summary>
        /// Appends an unwind stage to the pipeline.
        /// </summary>
        /// <typeparam name="TNewResult">The type of the new result.</typeparam>
        /// <param name="field">The field.</param>
        /// <param name="options">The options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TNewResult> Unwind<TNewResult>(FieldDefinition<TResult> field, AggregateUnwindOptions<TNewResult> options = null);

        /// <summary>
        /// Appends a vector search stage.
        /// </summary>
        /// <param name="field">The field.</param>
        /// <param name="queryVector">The query vector.</param>
        /// <param name="limit">The limit.</param>
        /// <param name="options">The vector search options.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IAggregateFluent<TResult> VectorSearch(
            FieldDefinition<TResult> field,
            QueryVector queryVector,
            int limit,
            VectorSearchOptions<TResult> options = null);
    }

    /// <summary>
    /// Fluent interface for aggregate.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    public interface IOrderedAggregateFluent<TResult> : IAggregateFluent<TResult>
    {
        /// <summary>
        /// Combines the current sort definition with an additional sort definition.
        /// </summary>
        /// <param name="newSort">The new sort.</param>
        /// <returns>The fluent aggregate interface.</returns>
        IOrderedAggregateFluent<TResult> ThenBy(SortDefinition<TResult> newSort);
    }
}
