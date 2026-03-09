using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Profiling;

public record GetProfilingCatalogRequest : IRequest<ProfilingCatalog>, IContractKey
{
    public string GetKey() => "profiling:catalog";
}
