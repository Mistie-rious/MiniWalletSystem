using Nethereum.JsonRpc.Client;

namespace WalletBackend.Services.TransactionService;

public class TimeoutRequestInterceptor : RequestInterceptor
{
    private readonly int _timeoutMilliseconds;

    public TimeoutRequestInterceptor(int timeoutMilliseconds)
    {
        _timeoutMilliseconds = timeoutMilliseconds;
    }

    public async Task<object> InterceptSendRequestAsync<T>(
        Func<RpcRequest, string, Task<T>> interceptedSendRequestAsync, 
        RpcRequest request, 
        string route = null)
    {
        using var cts = new CancellationTokenSource(_timeoutMilliseconds);
        try
        {
            return await interceptedSendRequestAsync(request, route);
        }
        catch (TaskCanceledException) when (cts.Token.IsCancellationRequested)
        {
            throw new RpcClientTimeoutException($"Request timed out after {_timeoutMilliseconds}ms");
        }
    }

    public async Task<object> InterceptSendRequestAsync<T>(
        Func<string, string, object[], Task<T>> interceptedSendRequestAsync, 
        string method, 
        string route = null, 
        params object[] paramList)
    {
        using var cts = new CancellationTokenSource(_timeoutMilliseconds);
        try
        {
            return await interceptedSendRequestAsync(method, route, paramList);
        }
        catch (TaskCanceledException) when (cts.Token.IsCancellationRequested)
        {
            throw new RpcClientTimeoutException($"Request timed out after {_timeoutMilliseconds}ms");
        }
    }
}