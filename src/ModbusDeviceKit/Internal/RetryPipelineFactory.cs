using Microsoft.Extensions.Logging;
using ModbusDeviceKit.Profiles;
using Polly;
using Polly.Retry;

namespace ModbusDeviceKit.Internal;

/// <summary>Builds the Polly pipeline that retries transient Modbus failures.</summary>
internal static class RetryPipelineFactory
{
    /// <summary>Name of the running operation, carried in the resilience context for logging.</summary>
    public static readonly ResiliencePropertyKey<string> OperationKey = new("ModbusDeviceKit.Operation");

    public static ResiliencePipeline Create(RetrySettings settings, ILogger logger, string deviceName)
    {
        if (settings.MaxRetries <= 0)
            return ResiliencePipeline.Empty;

        int maxAttempts = settings.MaxRetries + 1;
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ExceptionClassifier.IsTransient),
                MaxRetryAttempts = settings.MaxRetries,
                Delay = TimeSpan.FromMilliseconds(settings.DelayMs),
                MaxDelay = settings.MaxDelayMs > 0 ? TimeSpan.FromMilliseconds(settings.MaxDelayMs) : null,
                BackoffType = settings.Backoff switch
                {
                    RetryBackoff.Constant => DelayBackoffType.Constant,
                    RetryBackoff.Linear => DelayBackoffType.Linear,
                    _ => DelayBackoffType.Exponential,
                },
                UseJitter = settings.UseJitter,
                OnRetry = args =>
                {
                    string operation = args.Context.Properties.GetValue(OperationKey, "operation");
                    logger.LogWarning(
                        "{Device}: {Operation} failed on attempt {Attempt}/{MaxAttempts}: {Error} Retrying in {DelayMs} ms.",
                        deviceName, operation, args.AttemptNumber + 1, maxAttempts,
                        args.Outcome.Exception?.Message, (int)args.RetryDelay.TotalMilliseconds);
                    return default;
                },
            })
            .Build();
    }
}
