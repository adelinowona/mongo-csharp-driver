﻿/* Copyright 2010-present MongoDB Inc.
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
using FluentAssertions;
using MongoDB.Driver.TestHelpers;
using Xunit;

namespace MongoDB.Driver.Tests.Linq.Linq3Implementation.Jira
{
    public class CSharp1754Tests : LinqIntegrationTest<CSharp1754Tests.ClassFixture>
    {
        public CSharp1754Tests(ClassFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public void Test()
        {
            var collection = Fixture.Collection;
            var requiredMeta = new[] { "a", "b" };

            var queryable = collection.AsQueryable()
                .Where(x => x.Occurrences.Any(o => requiredMeta.All(i => o.Meta.Contains(i))));

            var stages = Translate(collection, queryable);
            AssertStages(stages, "{ $match : { Occurrences : { $elemMatch : { Meta : { $all : ['a', 'b'] } } } } }");

            var results = queryable.ToList();
            results.Select(r => r.Id).Should().Equal(2);
        }

        public class C
        {
            public int Id { get; set; }
            public Occurrence[] Occurrences { get; set; }
        }

        public class Occurrence
        {
            public string[] Meta { get; set; }
        }

        public sealed class ClassFixture : MongoCollectionFixture<C>
        {
            protected override IEnumerable<C> InitialData =>
            [
                new C { Id = 1, Occurrences = new[] { new Occurrence { Meta = new[] { "a" } } } },
                new C { Id = 2, Occurrences = new[] { new Occurrence { Meta = new[] { "a" } }, new Occurrence { Meta = new[] { "a", "b" } } } }
            ];
        }
    }
}
