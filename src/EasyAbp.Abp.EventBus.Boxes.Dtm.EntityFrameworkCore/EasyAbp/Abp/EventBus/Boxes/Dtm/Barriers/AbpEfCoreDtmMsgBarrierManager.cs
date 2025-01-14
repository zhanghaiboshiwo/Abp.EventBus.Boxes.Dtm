﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DtmCommon;
using EasyAbp.Abp.EventBus.Boxes.Dtm.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Volo.Abp.Uow.EntityFrameworkCore;

namespace EasyAbp.Abp.EventBus.Boxes.Dtm.Barriers;

public class AbpEfCoreDtmMsgBarrierManager : DtmMsgBarrierManagerBase<IEfCoreDbContext>,
    IAbpEfCoreDtmMsgBarrierManager, ITransientDependency
{
    protected AbpDtmEventBoxesOptions Options { get; }
    private ILogger<AbpEfCoreDtmMsgBarrierManager> Logger { get; }
    protected IDtmBarrierTableInitializer BarrierTableInitializer { get; }

    public AbpEfCoreDtmMsgBarrierManager(
        IOptions<AbpDtmEventBoxesOptions> options,
        ILogger<AbpEfCoreDtmMsgBarrierManager> logger,
        IDtmBarrierTableInitializer barrierTableInitializer)
    {
        Options = options.Value;
        Logger = logger;
        BarrierTableInitializer = barrierTableInitializer;
    }
    
    public override async Task EnsureInsertBarrierAsync(IEfCoreDbContext dbContext, string gid,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new AbpException("DTM barrier is for ABP transactional events.");
        }

        var affected = await InsertBarrierAsync(dbContext, gid, Constant.TYPE_MSG);
        
        Logger?.LogDebug("currentAffected: {currentAffected}", affected);

        if (affected == 0)
        {
            throw new DtmDuplicatedException();
        }
    }

    public override async Task<bool> TryInsertBarrierAsRollbackAsync(IEfCoreDbContext dbContext, string gid,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await InsertBarrierAsync(dbContext, gid, Constant.Barrier.MSG_BARRIER_REASON);
        }
        catch (Exception e)
        {
            Logger?.LogWarning(e, "Insert Barrier error, gid={gid}", gid);
            throw;
        }

        try
        {
            var special = BarrierSqlTemplates.DbProviderSpecialMapping.GetOrDefault(dbContext.Database.ProviderName);

            var reason = await dbContext.Database.GetDbConnection().QueryFirstOrDefaultAsync<string>(
                special.GetQueryPreparedSql(Options.BarrierTableName),
                new
                {
                    gid, branch_id = Constant.Barrier.MSG_BRANCHID, op = Constant.TYPE_MSG,
                    barrier_id = Constant.Barrier.MSG_BARRIER_ID
                });

            if (reason.Equals(Constant.Barrier.MSG_BARRIER_REASON))
            {
                return true;    // The "rollback" inserted succeed.
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Query Prepared error, gid={gid}", gid);
            throw;
        }

        return false;    // The "rollback" not inserted.
    }

    protected virtual async Task<int> InsertBarrierAsync(IEfCoreDbContext dbContext, string gid, string reason)
    {
        await BarrierTableInitializer.TryCreateTableAsync(dbContext);
        
        var special = BarrierSqlTemplates.DbProviderSpecialMapping.GetOrDefault(dbContext.Database.ProviderName);

        if (special is null)
        {
            throw new NotSupportedException(
                $"Database provider {dbContext.Database.ProviderName} is not supported by the event boxes!");
        }
        
        var sql = special.GetInsertIgnoreTemplate(Options.BarrierTableName);

        sql = special.GetPlaceHoldSQL(sql);

        var affected = await dbContext.Database.GetDbConnection().ExecuteAsync(
            sql,
            new
            {
                trans_type = Constant.TYPE_MSG, gid = gid, branch_id = Constant.Barrier.MSG_BRANCHID,
                op = Constant.TYPE_MSG, barrier_id = Constant.Barrier.MSG_BARRIER_ID, reason = reason
            },
            dbContext.Database.CurrentTransaction?.GetDbTransaction());

        return affected;
    }

    public override async Task<bool> TryInvokeEnsureInsertBarrierAsync(IDatabaseApi databaseApi, string gid,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidDatabaseApi<EfCoreDatabaseApi>(databaseApi))
        {
            return false;
        }

        await EnsureInsertBarrierAsync(((EfCoreDatabaseApi)databaseApi).DbContext, gid, cancellationToken);

        return true;
    }
}
