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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.Search;

namespace MongoDB.Driver
{
    internal abstract class AggregateFluent<TInput, TResult> : AggregateFluentBase<TResult>
    {
        // fields
        protected readonly AggregateOptions _options;
        protected readonly PipelineDefinition<TInput, TResult> _pipeline;
        protected readonly IClientSessionHandle _session;

        // constructors
        protected AggregateFluent(IClientSessionHandle session, PipelineDefinition<TInput, TResult> pipeline, AggregateOptions options)
        {
            _session = session; // can be null
            _pipeline = Ensure.IsNotNull(pipeline, nameof(pipeline));
            _options = Ensure.IsNotNull(options, nameof(options));
        }

        // properties
        public override AggregateOptions Options
        {
            get { return _options; }
        }

        public PipelineDefinition<TInput, TResult> Pipeline => _pipeline;

        public override IList<IPipelineStageDefinition> Stages
        {
            get { return _pipeline.Stages.ToList(); }
        }

        // methods
        public override IAggregateFluent<TNewResult> AppendStage<TNewResult>(PipelineStageDefinition<TResult, TNewResult> stage)
        {
            return WithPipeline(_pipeline.AppendStage(stage));
        }

        public override IAggregateFluent<TNewResult> As<TNewResult>(IBsonSerializer<TNewResult> newResultSerializer)
        {
            return WithPipeline(_pipeline.As(newResultSerializer));
        }

        public override IAggregateFluent<AggregateBucketResult<TValue>> Bucket<TValue>(
            AggregateExpressionDefinition<TResult, TValue> groupBy,
            IEnumerable<TValue> boundaries,
            AggregateBucketOptions<TValue> options = null)
        {
            return WithPipeline(_pipeline.Bucket(groupBy, boundaries, options));
        }

        public override IAggregateFluent<TNewResult> Bucket<TValue, TNewResult>(
            AggregateExpressionDefinition<TResult, TValue> groupBy,
            IEnumerable<TValue> boundaries,
            ProjectionDefinition<TResult, TNewResult> output,
            AggregateBucketOptions<TValue> options = null)
        {
            return WithPipeline(_pipeline.Bucket(groupBy, boundaries, output, options));
        }

        public override IAggregateFluent<AggregateBucketAutoResult<TValue>> BucketAuto<TValue>(
            AggregateExpressionDefinition<TResult, TValue> groupBy,
            int buckets,
            AggregateBucketAutoOptions options = null)
        {
            return WithPipeline(_pipeline.BucketAuto(groupBy, buckets, options));
        }

        public override IAggregateFluent<TNewResult> BucketAuto<TValue, TNewResult>(
            AggregateExpressionDefinition<TResult, TValue> groupBy,
            int buckets,
            ProjectionDefinition<TResult, TNewResult> output,
            AggregateBucketAutoOptions options = null)
        {
            return WithPipeline(_pipeline.BucketAuto(groupBy, buckets, output, options));
        }

        public override IAggregateFluent<ChangeStreamDocument<TResult>> ChangeStream(ChangeStreamStageOptions options = null)
        {
            return WithPipeline(_pipeline.ChangeStream(options));
        }

        public override IAggregateFluent<AggregateCountResult> Count()
        {
            return WithPipeline(_pipeline.Count());
        }

        public override IAggregateFluent<TResult> Densify(
            FieldDefinition<TResult> field,
            DensifyRange range,
            IEnumerable<FieldDefinition<TResult>> partitionByFields = null)
        {
            return WithPipeline(_pipeline.Densify(field, range, partitionByFields));
        }

        public override IAggregateFluent<TResult> Densify(
            FieldDefinition<TResult> field,
            DensifyRange range,
            params FieldDefinition<TResult>[] partitionByFields)
        {
            return WithPipeline(_pipeline.Densify(field, range, partitionByFields));
        }

        public override IAggregateFluent<TNewResult> Facet<TNewResult>(
            IEnumerable<AggregateFacet<TResult>> facets,
            AggregateFacetOptions<TNewResult> options = null)
        {
            return WithPipeline(_pipeline.Facet(facets, options));
        }

        public override IAggregateFluent<TNewResult> GraphLookup<TFrom, TConnectFrom, TConnectTo, TStartWith, TAsElement, TAs, TNewResult>(
            IMongoCollection<TFrom> from,
            FieldDefinition<TFrom, TConnectFrom> connectFromField,
            FieldDefinition<TFrom, TConnectTo> connectToField,
            AggregateExpressionDefinition<TResult, TStartWith> startWith,
            FieldDefinition<TNewResult, TAs> @as,
            FieldDefinition<TAsElement, int> depthField,
            AggregateGraphLookupOptions<TFrom, TAsElement, TNewResult> options = null)
        {
            return WithPipeline(_pipeline.GraphLookup(from, connectFromField, connectToField, startWith, @as, depthField, options));
        }

        public override IAggregateFluent<TNewResult> Group<TNewResult>(ProjectionDefinition<TResult, TNewResult> group)
        {
            return WithPipeline(_pipeline.Group(group));
        }

        public override IAggregateFluent<TResult> Limit(long limit)
        {
            return WithPipeline(_pipeline.Limit(limit));
        }

        public override IAggregateFluent<TNewResult> Lookup<TForeignDocument, TNewResult>(string foreignCollectionName, FieldDefinition<TResult> localField, FieldDefinition<TForeignDocument> foreignField, FieldDefinition<TNewResult> @as, AggregateLookupOptions<TForeignDocument, TNewResult> options)
        {
            Ensure.IsNotNull(foreignCollectionName, nameof(foreignCollectionName));
            var foreignCollection = Database.GetCollection<TForeignDocument>(foreignCollectionName);
            return WithPipeline(_pipeline.Lookup(foreignCollection, localField, foreignField, @as, options));
        }

        public override IAggregateFluent<TNewResult> Lookup<TForeignDocument, TAsElement, TAs, TNewResult>(
            IMongoCollection<TForeignDocument> foreignCollection,
            BsonDocument let,
            PipelineDefinition<TForeignDocument, TAsElement> lookupPipeline,
            FieldDefinition<TNewResult, TAs> @as,
            AggregateLookupOptions<TForeignDocument, TNewResult> options = null)
        {
            Ensure.IsNotNull(foreignCollection, nameof(foreignCollection));
            return WithPipeline(_pipeline.Lookup(foreignCollection, let, lookupPipeline, @as));
        }

        public override IAggregateFluent<TResult> Match(FilterDefinition<TResult> filter)
        {
            return WithPipeline(_pipeline.Match(filter));
        }

        public override IAsyncCursor<TOutput> Merge<TOutput>(IMongoCollection<TOutput> outputCollection, MergeStageOptions<TOutput> mergeOptions = null, CancellationToken cancellationToken = default)
        {
            Ensure.IsNotNull(outputCollection, nameof(outputCollection));
            mergeOptions = mergeOptions ?? new MergeStageOptions<TOutput>();
            var aggregate = WithPipeline(_pipeline.Merge<TInput, TResult, TOutput>(outputCollection, mergeOptions));
            return aggregate.ToCursor(cancellationToken);
        }

        public override async Task<IAsyncCursor<TOutput>> MergeAsync<TOutput>(IMongoCollection<TOutput> outputCollection, MergeStageOptions<TOutput> mergeOptions = null, CancellationToken cancellationToken = default)
        {
            Ensure.IsNotNull(outputCollection, nameof(outputCollection));
            mergeOptions = mergeOptions ?? new MergeStageOptions<TOutput>();
            var aggregate = WithPipeline(_pipeline.Merge<TInput, TResult, TOutput>(outputCollection, mergeOptions));
            return await aggregate.ToCursorAsync(cancellationToken).ConfigureAwait(false);
        }

        public override IAggregateFluent<TNewResult> OfType<TNewResult>(IBsonSerializer<TNewResult> newResultSerializer)
        {
            return WithPipeline(_pipeline.OfType(newResultSerializer));
        }

        public override IAsyncCursor<TResult> Out(IMongoCollection<TResult> outputCollection, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(outputCollection, nameof(outputCollection));
            var aggregate = WithPipeline(_pipeline.Out(outputCollection));
            return aggregate.ToCursor(cancellationToken);
        }

        public override IAsyncCursor<TResult> Out(string collectionName, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(collectionName, nameof(collectionName));
            var outputCollection = Database.GetCollection<TResult>(collectionName);
            return Out(outputCollection, cancellationToken);
        }

        public override IAsyncCursor<TResult> Out(IMongoCollection<TResult> outputCollection, TimeSeriesOptions timeSeriesOptions, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(outputCollection, nameof(outputCollection));
            var aggregate = WithPipeline(_pipeline.Out(outputCollection, timeSeriesOptions));
            return aggregate.ToCursor(cancellationToken);
        }

        public override IAsyncCursor<TResult> Out(string collectionName, TimeSeriesOptions timeSeriesOptions, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(collectionName, nameof(collectionName));
            var outputCollection = Database.GetCollection<TResult>(collectionName);
            return Out(outputCollection, timeSeriesOptions, cancellationToken);
        }

        public override Task<IAsyncCursor<TResult>> OutAsync(IMongoCollection<TResult> outputCollection, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(outputCollection, nameof(outputCollection));
            var aggregate = WithPipeline(_pipeline.Out(outputCollection));
            return aggregate.ToCursorAsync(cancellationToken);
        }

        public override Task<IAsyncCursor<TResult>> OutAsync(string collectionName, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(collectionName, nameof(collectionName));
            var outputCollection = Database.GetCollection<TResult>(collectionName);
            return OutAsync(outputCollection, cancellationToken);
        }

        public override Task<IAsyncCursor<TResult>> OutAsync(IMongoCollection<TResult> outputCollection, TimeSeriesOptions timeSeriesOptions, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(outputCollection, nameof(outputCollection));
            var aggregate = WithPipeline(_pipeline.Out(outputCollection, timeSeriesOptions));
            return aggregate.ToCursorAsync(cancellationToken);
        }

        public override Task<IAsyncCursor<TResult>> OutAsync(string collectionName, TimeSeriesOptions timeSeriesOptions, CancellationToken cancellationToken)
        {
            Ensure.IsNotNull(collectionName, nameof(collectionName));
            var outputCollection = Database.GetCollection<TResult>(collectionName);
            return OutAsync(outputCollection, timeSeriesOptions, cancellationToken);
        }

        public override IAggregateFluent<TNewResult> Project<TNewResult>(ProjectionDefinition<TResult, TNewResult> projection)
        {
            return WithPipeline(_pipeline.Project(projection));
        }

        public override IAggregateFluent<TNewResult> RankFusion<TNewResult>(
            Dictionary<string, PipelineDefinition<TResult, TNewResult>> pipelines,
            Dictionary<string, double> weights = null,
            RankFusionOptions<TNewResult> options = null)
        {
            return WithPipeline(_pipeline.RankFusion(pipelines, weights, options));
        }

        public override IAggregateFluent<TNewResult> RankFusion<TNewResult>(
            PipelineDefinition<TResult, TNewResult>[] pipelines,
            RankFusionOptions<TNewResult> options = null)
        {
            return WithPipeline(_pipeline.RankFusion(pipelines, options));
        }

        public override IAggregateFluent<TNewResult> RankFusion<TNewResult>(
            (PipelineDefinition<TResult, TNewResult>, double?)[] pipelinesWithWeights,
            RankFusionOptions<TNewResult> options = null)
        {
            return WithPipeline(_pipeline.RankFusion(pipelinesWithWeights, options));
        }

        public override IAggregateFluent<TNewResult> ReplaceRoot<TNewResult>(AggregateExpressionDefinition<TResult, TNewResult> newRoot)
        {
            return WithPipeline(_pipeline.ReplaceRoot(newRoot));
        }

        public override IAggregateFluent<TNewResult> ReplaceWith<TNewResult>(AggregateExpressionDefinition<TResult, TNewResult> newRoot)
        {
            return WithPipeline(_pipeline.ReplaceWith(newRoot));
        }

        public override IAggregateFluent<TResult> Sample(long size)
        {
            return WithPipeline(_pipeline.Sample(size));
        }

        public override IAggregateFluent<TResult> Search(
            SearchDefinition<TResult> searchDefinition,
            SearchHighlightOptions<TResult> highlight = null,
            string indexName = null,
            SearchCountOptions count = null,
            bool returnStoredSource = false,
            bool scoreDetails = false)
        {
            var searchOptions = new SearchOptions<TResult>()
            {
                CountOptions = count,
                Highlight = highlight,
                IndexName = indexName,
                ReturnStoredSource = returnStoredSource,
                ScoreDetails = scoreDetails
            };

            return WithPipeline(_pipeline.Search(searchDefinition, searchOptions));
        }

        public override IAggregateFluent<TResult> Search(
            SearchDefinition<TResult> searchDefinition,
            SearchOptions<TResult> searchOptions)
        {
            return WithPipeline(_pipeline.Search(searchDefinition, searchOptions));
        }

        public override IAggregateFluent<SearchMetaResult> SearchMeta(
            SearchDefinition<TResult> searchDefinition,
            string indexName = null,
            SearchCountOptions count = null)
        {
            return WithPipeline(_pipeline.SearchMeta(searchDefinition, indexName, count));
        }

        public override IAggregateFluent<TResult> Set(SetFieldDefinitions<TResult> fields)
        {
            return WithPipeline(_pipeline.Set(fields));
        }

        public override IAggregateFluent<BsonDocument> SetWindowFields<TWindowFields>(
            AggregateExpressionDefinition<ISetWindowFieldsPartition<TResult>, TWindowFields> output)
        {
            return WithPipeline(_pipeline.SetWindowFields(output));
        }

        public override IAggregateFluent<BsonDocument> SetWindowFields<TPartitionBy, TWindowFields>(
            AggregateExpressionDefinition<TResult, TPartitionBy> partitionBy,
            AggregateExpressionDefinition<ISetWindowFieldsPartition<TResult>, TWindowFields> output)
        {
            return WithPipeline(_pipeline.SetWindowFields(partitionBy, output));
        }

        public override IAggregateFluent<BsonDocument> SetWindowFields<TPartitionBy, TWindowFields>(
            AggregateExpressionDefinition<TResult, TPartitionBy> partitionBy,
            SortDefinition<TResult> sortBy,
            AggregateExpressionDefinition<ISetWindowFieldsPartition<TResult>, TWindowFields> output)
        {
            return WithPipeline(_pipeline.SetWindowFields(partitionBy, sortBy, output));
        }

        public override IAggregateFluent<TResult> Skip(long skip)
        {
            return WithPipeline(_pipeline.Skip(skip));
        }

        public override IAggregateFluent<TResult> Sort(SortDefinition<TResult> sort)
        {
            return WithPipeline(_pipeline.Sort(sort));
        }

        public override IAggregateFluent<AggregateSortByCountResult<TId>> SortByCount<TId>(AggregateExpressionDefinition<TResult, TId> id)
        {
            return WithPipeline(_pipeline.SortByCount(id));
        }

        public override IOrderedAggregateFluent<TResult> ThenBy(SortDefinition<TResult> newSort)
        {
            Ensure.IsNotNull(newSort, nameof(newSort));
            var stages = _pipeline.Stages.ToList();
            var oldSortStage = (SortPipelineStageDefinition<TResult>)stages[stages.Count - 1];
            var oldSort = oldSortStage.Sort;
            var combinedSort = Builders<TResult>.Sort.Combine(oldSort, newSort);
            var combinedSortStage = PipelineStageDefinitionBuilder.Sort(combinedSort);
            stages[stages.Count - 1] = combinedSortStage;
            var newPipeline = new PipelineStagePipelineDefinition<TInput, TResult>(stages);
            return (IOrderedAggregateFluent<TResult>)WithPipeline(newPipeline);
        }

        public override IAggregateFluent<TResult> UnionWith<TWith>(
            IMongoCollection<TWith> withCollection,
            PipelineDefinition<TWith, TResult> withPipeline = null)
        {
            Ensure.IsNotNull(withCollection, nameof(withCollection));
            return WithPipeline(_pipeline.UnionWith(withCollection, withPipeline));
        }

        public override IAggregateFluent<TNewResult> Unwind<TNewResult>(FieldDefinition<TResult> field, IBsonSerializer<TNewResult> newResultSerializer)
        {
            return WithPipeline(_pipeline.Unwind(field, new AggregateUnwindOptions<TNewResult> { ResultSerializer = newResultSerializer }));
        }

        public override IAggregateFluent<TNewResult> Unwind<TNewResult>(FieldDefinition<TResult> field, AggregateUnwindOptions<TNewResult> options)
        {
            return WithPipeline(_pipeline.Unwind(field, options));
        }

        public override IAggregateFluent<TResult> VectorSearch(
            FieldDefinition<TResult> field,
            QueryVector queryVector,
            int limit,
            VectorSearchOptions<TResult> options = null)
        {
            return WithPipeline(_pipeline.VectorSearch(field, queryVector, limit, options));
        }

        public override string ToString()
        {
            return $"aggregate({_pipeline.ToString()})";
        }

        protected abstract IAggregateFluent<TNewResult> WithPipeline<TNewResult>(PipelineDefinition<TInput, TNewResult> pipeline);
    }

    internal class CollectionAggregateFluent<TDocument, TResult> : AggregateFluent<TDocument, TResult>
    {
        // private fields
        private readonly IMongoCollection<TDocument> _collection;

        // constructors
        public CollectionAggregateFluent(
            IClientSessionHandle session,
            IMongoCollection<TDocument> collection,
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions options)
            : base(session, pipeline, options)
        {
            _collection = Ensure.IsNotNull(collection, nameof(collection));
        }

        // public properties
        public override IMongoDatabase Database => _collection.Database;

        // public methods
        public override void ToCollection(CancellationToken cancellationToken)
        {
            if (_session == null)
            {
                _collection.AggregateToCollection(_pipeline, _options, cancellationToken);
            }
            else
            {
                _collection.AggregateToCollection(_session, _pipeline, _options, cancellationToken);
            }
        }

        public override Task ToCollectionAsync(CancellationToken cancellationToken)
        {
            if (_session == null)
            {
                return _collection.AggregateToCollectionAsync(_pipeline, _options, cancellationToken);
            }
            else
            {
                return _collection.AggregateToCollectionAsync(_session, _pipeline, _options, cancellationToken);
            }
        }

        public override IAsyncCursor<TResult> ToCursor(CancellationToken cancellationToken)
        {
            if (_session == null)
            {
                return _collection.Aggregate(_pipeline, _options, cancellationToken);
            }
            else
            {
                return _collection.Aggregate(_session, _pipeline, _options, cancellationToken);
            }
        }

        public override Task<IAsyncCursor<TResult>> ToCursorAsync(CancellationToken cancellationToken)
        {
            if (_session == null)
            {
                return _collection.AggregateAsync(_pipeline, _options, cancellationToken);
            }
            else
            {
                return _collection.AggregateAsync(_session, _pipeline, _options, cancellationToken);
            }
        }

        // protected methods
        protected override IAggregateFluent<TNewResult> WithPipeline<TNewResult>(PipelineDefinition<TDocument, TNewResult> pipeline)
        {
            return new CollectionAggregateFluent<TDocument, TNewResult>(_session, _collection, pipeline, _options);
        }
    }

    internal class DatabaseAggregateFluent<TResult> : AggregateFluent<NoPipelineInput, TResult>
    {
        // private fields
        private readonly IMongoDatabase _database;

        // constructors
        public DatabaseAggregateFluent(
            IClientSessionHandle session,
            IMongoDatabase database,
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions options)
            : base(session, pipeline, options)
        {
            _database = Ensure.IsNotNull(database, nameof(database));
        }

        // public properties
        public override IMongoDatabase Database => _database;

        // public methods
        public override void ToCollection(CancellationToken cancellationToken)
        {
            if (_session == null)
            {
                _database.AggregateToCollection(_pipeline, _options, cancellationToken);
            }
            else
            {
                _database.AggregateToCollection(_session, _pipeline, _options, cancellationToken);
            }
        }

        public override Task ToCollectionAsync(CancellationToken cancellationToken)
        {
            if (_session == null)
            {
                return _database.AggregateToCollectionAsync(_pipeline, _options, cancellationToken);
            }
            else
            {
                return _database.AggregateToCollectionAsync(_session, _pipeline, _options, cancellationToken);
            }
        }

        public override IAsyncCursor<TResult> ToCursor(CancellationToken cancellationToken)
        {
            if (_session == null)
            {
                return _database.Aggregate(_pipeline, _options, cancellationToken);
            }
            else
            {
                return _database.Aggregate(_session, _pipeline, _options, cancellationToken);
            }
        }

        public override Task<IAsyncCursor<TResult>> ToCursorAsync(CancellationToken cancellationToken)
        {
            if (_session == null)
            {
                return _database.AggregateAsync(_pipeline, _options, cancellationToken);
            }
            else
            {
                return _database.AggregateAsync(_session, _pipeline, _options, cancellationToken);
            }
        }

        // protected methods
        protected override IAggregateFluent<TNewResult> WithPipeline<TNewResult>(PipelineDefinition<NoPipelineInput, TNewResult> pipeline)
        {
            return new DatabaseAggregateFluent<TNewResult>(_session, _database, pipeline, _options);
        }
    }
}
