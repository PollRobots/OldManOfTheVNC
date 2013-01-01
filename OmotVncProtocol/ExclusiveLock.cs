// -----------------------------------------------------------------------------
// <copyright file="ExclusiveLock.cs" company="Paul C. Roberts">
//  Copyright 2012 Paul C. Roberts
//
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file 
//  except in compliance with the License. You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software distributed under the 
//  License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, 
//  either express or implied. See the License for the specific language governing permissions and 
//  limitations under the License.
// </copyright>
// -----------------------------------------------------------------------------

namespace PollRobots.OmotVncProtocol
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class ExclusiveLock
    {
        private LockEntry currentEntry;

        private class LockEntry : IDisposable
        {
            private readonly TaskCompletionSource<bool> source = new TaskCompletionSource<bool>();
            private readonly ExclusiveLock parentLock;
#if NETFX_CORE

            public LockEntry(ExclusiveLock parent)
            {
                this.parentLock = parent;
            }

#else
            private readonly StackTrace stackTrace;

            public LockEntry(ExclusiveLock parent, StackTrace trace)
            {
                this.parentLock = parent;
                this.stackTrace = trace;
            }
#endif

            public void Dispose()
            {
                this.source.SetResult(true);
                Interlocked.CompareExchange(ref this.parentLock.currentEntry, null, this);
            }

            public Task Wait()
            {
                return source.Task;
            }
        }

        public async Task<IDisposable> Enter()
        {
#if NETFX_CORE
            LockEntry next = new LockEntry(this);
#else
            LockEntry next = new LockEntry(this, new StackTrace());
#endif

            for (; ; )
            {
                LockEntry current = this.currentEntry;
                if (current != null)
                {
                    await current.Wait();
                    await Task.Yield();
                }
                else if (null == Interlocked.CompareExchange(ref this.currentEntry, next, null))
                {
                    return next;
                }
            }
        }
    }
}