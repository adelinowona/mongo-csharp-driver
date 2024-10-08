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

using System.Linq;
using FluentAssertions;
using MongoDB.Driver.Linq;
using Xunit;

namespace MongoDB.Driver.Tests.Linq.Linq3Implementation.Jira
{
    public class CSharp5229Tests : Linq3IntegrationTest
    {
        [Fact]
        public void Using_same_anonymous_type_twice_should_work()
        {
            var collection = GetCollection();

            var queryable = collection.AsQueryable()
                .Select(x => new { X = x.X })
                .Select(x => new { X = x.X })
                .Select(x => x.X);

            var stages = Translate(collection, queryable);
            AssertStages(
                stages,
                "{ $project : { X : '$X', _id : 0 } }",
                "{ $project : { X : '$X', _id : 0 } }",
                "{ $project : { _v : '$X', _id : 0 } }");

            var result = queryable.First();
            result.Should().Be(1);
        }

        private IMongoCollection<C> GetCollection()
        {
            var collection = GetCollection<C>("test");
            CreateCollection(
                collection,
                new C { Id = 1, X = 1 });
            return collection;
        }

        private class C
        {
            public int Id { get; set; }
            public int X { get; set; }
        }
    }
}
