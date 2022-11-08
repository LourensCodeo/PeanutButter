using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PeanutButter.Utils
{
    /// <summary>
    /// Provides common functionality to retry logic
    /// with configurable delay
    /// </summary>
    public static class RetryExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        /// <param name="maxRetries"></param>
        /// <param name="retryDelays"></param>
        public static void RunWithRetries(
            this Action action,
            int maxRetries,
            params TimeSpan[] retryDelays
        )
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var func = new Func<bool>(() =>
            {
                action.Invoke();
                return true;
            });
            func.RunWithRetries(maxRetries, retryDelays);
        }

        /// <summary>
        /// Runs the provided function with the requested
        /// number of retries and provided backoff delays
        /// </summary>
        /// <param name="func"></param>
        /// <param name="maxRetries"></param>
        /// <param name="retryDelays"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static T RunWithRetries<T>(
            this Func<T> func,
            int maxRetries,
            params TimeSpan[] retryDelays
        )
        {
            if (func is null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            if (maxRetries < 1)
            {
                throw new ArgumentException(
                    $"maxRetries must be at least 1 (provided value was: {maxRetries})",
                    nameof(maxRetries)
                );
            }

            if (retryDelays.Length == 0)
            {
                retryDelays = new[] { TimeSpan.FromSeconds(0) };
            }

            var lastDelay = retryDelays.Last();
            var delayQueue = new Queue<TimeSpan>(retryDelays);

            for (var i = 0; i < maxRetries; i++)
            {
                try
                {
                    return func.Invoke();
                }
                catch
                {
                    if (i == maxRetries - 1)
                    {
                        throw;
                    }

                    Thread.Sleep(
                        delayQueue.DequeueOrDefault(fallback: lastDelay)
                    );
                }
            }

            throw new InvalidOperationException(
                "Should never get here"
            );
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="asyncAction"></param>
        /// <param name="maxRetries"></param>
        /// <param name="retryDelays"></param>
        public static async Task RunWithRetries(
            this Func<Task> asyncAction,
            int maxRetries,
            params TimeSpan[] retryDelays
        )
        {
            if (asyncAction is null)
            {
                throw new ArgumentNullException(nameof(asyncAction));
            }

            var func = new Func<Task<bool>>(async () =>
            {
                await asyncAction.Invoke();
                return true;
            });
            await func.RunWithRetries(maxRetries, retryDelays);
        }

        /// <summary>
        /// Runs the provided function with the requested
        /// number of retries and provided backoff delays
        /// </summary>
        /// <param name="func"></param>
        /// <param name="maxRetries"></param>
        /// <param name="retryDelays"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static async Task<T> RunWithRetries<T>(
            this Func<Task<T>> func,
            int maxRetries,
            params TimeSpan[] retryDelays
        )
        {
            if (func is null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            if (maxRetries < 1)
            {
                throw new ArgumentException(
                    $"maxRetries must be at least 1 (provided value was: {maxRetries})",
                    nameof(maxRetries)
                );
            }

            if (retryDelays.Length == 0)
            {
                retryDelays = new[] { TimeSpan.FromSeconds(0) };
            }

            var lastDelay = retryDelays.Last();
            var delayQueue = new Queue<TimeSpan>(retryDelays);

            for (var i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await func.Invoke();
                }
                catch
                {
                    if (i == maxRetries - 1)
                    {
                        throw;
                    }

                    Thread.Sleep(
                        delayQueue.DequeueOrDefault(fallback: lastDelay)
                    );
                }
            }

            throw new InvalidOperationException(
                "Should never get here"
            );
        }
        
        
    }
}