using NLog;
using ILogger = NLog.ILogger;

namespace vgt_saga_orders;

/// <summary>
/// Utils decoding secrets from the configuration files or environmental variables
/// </summary>
public static class SecretUtils
{
    /// <summary>
    /// Gets SQL connection data from the configuration and merges that data into the connection string
    /// </summary>
    /// <param name="config"> configuration of the application </param>
    /// <param name="dbName"> Name of the database env variable to use for the connection</param>
    /// <param name="log"> logger to log to errors</param>
    /// <returns> Merged connection string </returns>
    /// <exception cref="ArgumentException"> Thrown if configuration param was not found </exception>
    public static string GetConnectionString(IConfiguration config, string dbName, Logger log)
    {
        var dbServer = string.IsNullOrEmpty(config.GetValue<string>("DB_SERVER"))
            ? ThrowException<string>("DB_SERVER", log)
            : config.GetValue<string>("DB_SERVER")!;
        var db = string.IsNullOrEmpty(config.GetValue<string>(dbName))
            ? ThrowException<string>(dbName, log)
            : config.GetValue<string>(dbName)!;
        var dbUser = string.IsNullOrEmpty(config.GetValue<string>("DB_USER"))
            ? ThrowException<string>("DB_USER", log)
            : config.GetValue<string>("DB_USER")!;
        var password = string.IsNullOrEmpty(config.GetValue<string>("DB_PASSWORD"))
            ? ThrowException<string>("DB_PASSWORD", log)
            : config.GetValue<string>("DB_PASSWORD")!;
        
        return $"Host={dbServer}; Database={db}; Username={dbUser}; Password={password};";
    }

    /// <summary>
    /// Logs, creates and throws the exception that the specified variable is not present in the config
    /// </summary>
    /// <param name="argument"> Variable not present </param>
    /// <param name="log"></param>
    /// <typeparam name="T"> type to "return" </typeparam>
    /// <returns></returns>
    /// <exception cref="ArgumentException"> Which variable is missing </exception>
    private static T ThrowException<T>(string argument, Logger log)
    {
        log.Error("{p}Couldn't load the data needed from env variables for the connection. Var: {e}", "SecretsUtils|",
            argument);
        throw new ArgumentException(argument);
    }
    
}