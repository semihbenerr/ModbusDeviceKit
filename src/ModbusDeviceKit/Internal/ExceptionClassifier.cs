using System.Net.Sockets;
using NModbus;

namespace ModbusDeviceKit.Internal;

internal enum FailureKind
{
    /// <summary>Not a communication failure (programming error, cancellation…): never retried nor wrapped.</summary>
    None,
    Timeout,
    Connection,
    Communication,
    SlaveTransient,
    SlaveRejected,
}

/// <summary>Decides which failures are retried and how they are reported.</summary>
internal static class ExceptionClassifier
{
    public static FailureKind Classify(Exception exception) => exception switch
    {
        DeviceProfileException => FailureKind.None,
        DeviceSlaveException s => ClassifySlaveCode(s.ExceptionCode),
        SlaveException s => ClassifySlaveCode(s.SlaveExceptionCode),
        DeviceTimeoutException => FailureKind.Timeout,
        DeviceConnectionException => FailureKind.Connection,
        DeviceCommunicationException => FailureKind.Communication,
        TimeoutException => FailureKind.Timeout,
        SocketException se => se.SocketErrorCode == SocketError.TimedOut ? FailureKind.Timeout : FailureKind.Connection,
        IOException io when IsSocketTimeout(io) => FailureKind.Timeout,
        IOException => FailureKind.Communication,
        _ => FailureKind.None,
    };

    public static bool IsTransient(Exception exception) =>
        Classify(exception) is FailureKind.Timeout or FailureKind.Connection or FailureKind.Communication or FailureKind.SlaveTransient;

    /// <summary>A <see cref="System.Net.Sockets.NetworkStream"/> read timeout surfaces as an IOException wrapping SocketError.TimedOut.</summary>
    public static bool IsSocketTimeout(IOException exception)
    {
        for (Exception? e = exception.InnerException; e is not null; e = e.InnerException)
        {
            if (e is SocketException { SocketErrorCode: SocketError.TimedOut } or TimeoutException)
                return true;
        }

        return false;
    }

    public static bool TryGetSlaveError(Exception exception, out byte functionCode, out byte exceptionCode)
    {
        switch (exception)
        {
            case DeviceSlaveException s:
                functionCode = s.FunctionCode;
                exceptionCode = s.ExceptionCode;
                return true;
            case SlaveException s:
                functionCode = s.FunctionCode;
                exceptionCode = s.SlaveExceptionCode;
                return true;
            default:
                functionCode = 0;
                exceptionCode = 0;
                return false;
        }
    }

    // Code 11 means the gateway got no answer from the target: for the caller that is a timeout.
    private static FailureKind ClassifySlaveCode(byte code) => code switch
    {
        11 => FailureKind.Timeout,
        _ when DeviceSlaveException.IsTransientCode(code) => FailureKind.SlaveTransient,
        _ => FailureKind.SlaveRejected,
    };
}
