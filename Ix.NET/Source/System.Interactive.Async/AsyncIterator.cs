using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace System.Linq
{
    public static partial class AsyncEnumerable
    {
        internal abstract class AsyncIterator<TSource> : IAsyncEnumerable<TSource>, IAsyncEnumerator<TSource>
        {
            public enum State
            {
                New = 0,
                Allocated = 1,
                Iterating = 2,
                Disposed = -1,
            }

            private readonly int threadId;
            internal State state = State.New;
            internal TSource current;
            private CancellationTokenSource cancellationTokenSource;

            protected AsyncIterator()
            {
                threadId = Environment.CurrentManagedThreadId;
            }

            public abstract AsyncIterator<TSource> Clone();

            public IAsyncEnumerator<TSource> GetEnumerator()
            {
                var enumerator = state == State.New && threadId == Environment.CurrentManagedThreadId ? this : Clone();

                enumerator.state = State.Allocated;
                enumerator.cancellationTokenSource = new CancellationTokenSource();
                return enumerator;
            }

            
            public virtual void Dispose()
            {
                if (!cancellationTokenSource.IsCancellationRequested)
                {
                    cancellationTokenSource.Cancel();
                }
                cancellationTokenSource.Dispose();
                current = default(TSource);
                state = State.Disposed;
            }

            private void Cancel()
            {
                if (!cancellationTokenSource.IsCancellationRequested)
                {
                    cancellationTokenSource.Cancel();
                }
         //       Dispose();
                Debug.WriteLine("Canceled");
            }

            public TSource Current => current;

            public Task<bool> MoveNext(CancellationToken cancellationToken)
            {

                

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellationTokenSource.Token);

                var reg = cts.Token.Register(() =>
                                             {
                                                 Debug.WriteLine("Linked token cancelled");
                                                 Dispose();
                                             });
                
                var tcs = new TaskCompletionSource<bool>(Tuple.Create(cts, reg)); ;

                var task = MoveNextCore(cts.Token);                
                task.ContinueWith((t, _) =>
                                  {
                                      Debug.WriteLine("Continue With after task");

                                      reg.Dispose();
                                      cts.Dispose();

                                      // Exception, or canceled
                                      if (t.IsFaulted || t.IsCanceled)
                                      {
                                          if (t.IsCanceled)
                                              tcs.SetCanceled();
                                          else if (t.IsFaulted)
                                              tcs.SetException(t.Exception.InnerExceptions);



                                          Dispose();
                                      }
                                      else
                                      {
                                          tcs.SetResult(t.Result);
                                      }

                                  }, null, TaskContinuationOptions.ExecuteSynchronously);

                return tcs.Task;

                //{
                //    try
                //    {
                //        var result = await MoveNextCore(cts.Token).ConfigureAwait(false);
                //        return result;
                //    }
                //    catch
                //    {
                //        Dispose();
                //        throw;
                //    }
                //}
            }

            public abstract Task<bool> MoveNextCore(CancellationToken cancellationToken);

            public virtual IAsyncEnumerable<TResult> Select<TResult>(Func<TSource, TResult> selector)
            {
                return new SelectEnumerableAsyncIterator<TSource, TResult>(this, selector);
            }

            public virtual IAsyncEnumerable<TSource> Where(Func<TSource, bool> predicate)
            {
                return new WhereEnumerableAsyncIterator<TSource>(this, predicate);
            }
        }
    }
}
