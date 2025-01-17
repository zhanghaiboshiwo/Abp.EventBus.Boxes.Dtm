﻿using System.Threading.Tasks;
using EasyAbp.Abp.EventBus.Boxes.Dtm.Options;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace EasyAbp.Abp.EventBus.Boxes.Dtm;

public class DefaultActionApiTokenChecker : IActionApiTokenChecker, ITransientDependency
{
    private readonly AbpDtmGrpcOptions _options;

    public DefaultActionApiTokenChecker(IOptions<AbpDtmGrpcOptions> options)
    {
        _options = options.Value;
    }
    
    public virtual Task<bool> IsCorrectAsync(string token)
    {
        return Task.FromResult(token == _options.ActionApiToken);
    }
}