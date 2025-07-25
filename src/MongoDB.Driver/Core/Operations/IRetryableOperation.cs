﻿/* Copyright 2017-present MongoDB Inc.
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

using System.Threading.Tasks;

namespace MongoDB.Driver.Core.Operations
{
    internal interface IExecutableInRetryableReadContext<TResult>
    {
        TResult Execute(OperationContext operationContext, RetryableReadContext context);
        Task<TResult> ExecuteAsync(OperationContext operationContext, RetryableReadContext context);
    }

    internal interface IExecutableInRetryableWriteContext<TResult>
    {
        TResult Execute(OperationContext operationContext, RetryableWriteContext context);
        Task<TResult> ExecuteAsync(OperationContext operationContext, RetryableWriteContext context);
    }

    internal interface IRetryableReadOperation<TResult> : IExecutableInRetryableReadContext<TResult>
    {
        TResult ExecuteAttempt(OperationContext operationContext, RetryableReadContext context, int attempt, long? transactionNumber);
        Task<TResult> ExecuteAttemptAsync(OperationContext operationContext, RetryableReadContext context, int attempt, long? transactionNumber);
    }

    internal interface IRetryableWriteOperation<TResult> : IExecutableInRetryableWriteContext<TResult>
    {
        WriteConcern WriteConcern { get; }

        TResult ExecuteAttempt(OperationContext operationContext, RetryableWriteContext context, int attempt, long? transactionNumber);
        Task<TResult> ExecuteAttemptAsync(OperationContext operationContext, RetryableWriteContext context, int attempt, long? transactionNumber);
    }
}
