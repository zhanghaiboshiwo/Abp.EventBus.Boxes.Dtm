﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using EasyAbp.Abp.EventBus.Boxes.Dtm.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace EasyAbp.Abp.EventBus.Boxes.Dtm.Barriers;

public class DtmBarrierTableInitializer : IDtmBarrierTableInitializer, ISingletonDependency
{
    protected IServiceProvider ServiceProvider { get; }
    protected IUnitOfWorkManager UnitOfWorkManager { get; }
    private ILogger<DtmBarrierTableInitializer> Logger { get; }
    private ConcurrentDictionary<string, bool> CreatedConnectionStrings { get; } = new();
    
    protected AbpDtmEventBoxesOptions Options { get; }

    public DtmBarrierTableInitializer(
        IServiceProvider serviceProvider,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<DtmBarrierTableInitializer> logger,
        IOptions<AbpDtmEventBoxesOptions> options)
    {
        ServiceProvider = serviceProvider;
        UnitOfWorkManager = unitOfWorkManager;
        Logger = logger;
        Options = options.Value;
    }
    
    public virtual async Task TryCreateTableAsync(IEfCoreDbContext dbContext)
    {
        var connectionString = dbContext.Database.GetConnectionString();
        
        if (CreatedConnectionStrings.ContainsKey(connectionString!))
        {
            return;
        }
        
        var special = BarrierSqlTemplates.DbProviderSpecialMapping.GetOrDefault(dbContext.Database.ProviderName);

        Logger.LogInformation("DtmBarrierTableInitializer found database provider: {databaseName}",
            dbContext.Database.ProviderName);

        if (special is null)
        {
            throw new NotSupportedException(
                $"Database provider {dbContext.Database.ProviderName} is not supported by the DTM event boxes!");
        }

        var sql = special.GetCreateBarrierTableSql(Options);

        using var newUow = UnitOfWorkManager.Begin(requiresNew: true);
        
        var dbContextProviderType = typeof(IDbContextProvider<>).MakeGenericType(dbContext.GetType());
        var dbContextProvider = ServiceProvider.GetRequiredService(dbContextProviderType);
        dynamic task =
            dbContextProviderType.GetMethod("GetDbContextAsync")!.Invoke(
                dbContextProvider, null);

        IEfCoreDbContext newDbContext = await task!;
            
        await newDbContext.Database.GetDbConnection().ExecuteAsync(sql, null);

        CreatedConnectionStrings[connectionString] = true;
    }
}