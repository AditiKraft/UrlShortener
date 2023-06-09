﻿using LinqKit;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UrlShortener.Domain;
using UrlShortener.Persistence;
using UrlShortener.Persistence.Interfaces;
using UrlShortener.Shared.Interfaces;
using UrlShortener.Shared.Models;

namespace UrlShortener.Applications.Implementations
{
    public class ShortUrlService : IShortUrlService
    {

        private readonly Container _containerClient;
        private readonly ILogger<ShortUrlService> _logger;
        private readonly bool _debugMode;

        public ShortUrlService(ICosmosDbClient cosmosDbClient,
            IConfiguration configuration,

            ILogger<ShortUrlService> logger

        )
        {
            _debugMode = configuration.GetValue<bool>("DebugMode");
            _logger = logger;
            _containerClient = cosmosDbClient.GetContainerClient();
        }

        public async Task<GetShortUrlsResponse> GetShortUrls(GetShortUrlsRequest req, CancellationToken ct)
        {
            var predicate = PredicateBuilder.New<ShortUrl>(true);
            predicate = predicate.And(p => p.EntityKind == EntityKind.ShortUrl);
            if (!string.IsNullOrWhiteSpace(req.Query))
            {
                var reqQuery = req.Query;
                var partitionValue = reqQuery.FirstOrDefault().ToString().ToLower();
                predicate = predicate.And(p =>
                    p.ShortName.Contains(reqQuery)
                    ||
                    p.DestinationUrl.Contains(reqQuery)
                );
                predicate = predicate.And(p => p.PartitionValue == partitionValue);
            }
            var queryable = _containerClient
                .GetItemLinqQueryable<ShortUrl>()
                .Where(predicate)
                .OrderByDescending(c => c.LastUpdateDateTime);

            var linqQuery = queryable
                .Skip(req.SkipCount)
                .Take(req.MaxResultCount);

            var shortUrlsTask =
                CosmosSqlHelper<ShortUrl>.ToListAsync(_containerClient, linqQuery, _logger,
                    _debugMode);
            var shortUrlsCount =
                queryable.CountAsync(ct);

            return new GetShortUrlsResponse()
            {
                TotalCount = await shortUrlsCount,
                Items = (await shortUrlsTask).Select(c => new ShortUrlsResponse
                {
                    ShortName = c.ShortName,
                    DestinationUrl = c.DestinationUrl,
                    LastUpdateDateTime = c.LastUpdateDateTime,
                    CreationDateTime = c.CreationDateTime,
                }).ToList()
            };
        }

        public async Task<CreateOrUpdateShortUrlResponse> CreateShortUrl(CreateOrUpdateShortUrlRequest req, CancellationToken ct)
        {
            var unixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var partitionValue = req.ShortName.FirstOrDefault().ToString().ToLower();
            await _containerClient.CreateItemAsync(new ShortUrl()
            {
                ShortName = req.ShortName,
                DestinationUrl = req.DestinationUrl,
                LastUpdateDateTime = unixTimeMilliseconds,
                CreationDateTime = unixTimeMilliseconds,
                PartitionValue = partitionValue
            }, cancellationToken: ct);

            return new CreateOrUpdateShortUrlResponse()
            {
                ShortName = req.ShortName,
                DestinationUrl = req.DestinationUrl,
                LastUpdateDateTime = unixTimeMilliseconds,
                CreationDateTime = unixTimeMilliseconds,
            };
        }

        public async Task<ShortUrlsResponse?> GetShortUrl(string shortName, CancellationToken ct)
        {

            var partitionValue = shortName.FirstOrDefault().ToString().ToLower();
            var queryable = _containerClient
                .GetItemLinqQueryable<ShortUrl>()
                .Where(c => c.PartitionValue == partitionValue && c.ShortName.ToLower() == shortName.ToLower());

            var linqQuery = queryable;

            var shortUrls = await
                CosmosSqlHelper<ShortUrl>.ToListAsync(_containerClient, linqQuery, _logger,
                    _debugMode);

            if (shortUrls.Count > 0)
            {
                return new ShortUrlsResponse()
                {
                    ShortName = shortUrls[0].ShortName,
                    DestinationUrl = shortUrls[0].DestinationUrl,
                    LastUpdateDateTime = shortUrls[0].LastUpdateDateTime,
                    CreationDateTime = shortUrls[0].CreationDateTime,
                };
            }
            return null;
        }

        public async Task DeleteShortUrl(DeleteShortUrlRequest req, CancellationToken ct)
        {
            var partitionValue = req.ShortName.FirstOrDefault().ToString().ToLower();
            await _containerClient.DeleteItemAsync<ShortUrl>(req.ShortName, new PartitionKey(partitionValue), cancellationToken: ct);
        }

        public async Task<CreateOrUpdateShortUrlResponse?> UpdateShortUrl(CreateOrUpdateShortUrlRequest req, CancellationToken ct)
        {
            var partitionValue = req.ShortName.FirstOrDefault().ToString().ToLower();
            var queryable = _containerClient
                .GetItemLinqQueryable<ShortUrl>()
                .Where(c => c.PartitionValue == partitionValue && c.ShortName.ToLower() == req.ShortName.ToLower());

            var linqQuery = queryable;

            var shortUrls = await
                CosmosSqlHelper<ShortUrl>.ToListAsync(_containerClient, linqQuery, _logger,
                    _debugMode);

            if (shortUrls.Any())
            {
                
                List<PatchOperation> patchOperations = new List<PatchOperation>
                {
                    PatchOperation.Set($"/DestinationUrl",req.DestinationUrl),
                    PatchOperation.Set($"/LastUpdateDateTime", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                };
              var res=  await _containerClient.PatchItemAsync<ShortUrl>(
                    id: shortUrls[0].ShortName,
                    partitionKey: new PartitionKey(partitionValue),
                    patchOperations: patchOperations,cancellationToken:ct);


                return new CreateOrUpdateShortUrlResponse()
                {
                    ShortName =res.Resource.ShortName,
                    CreationDateTime = res.Resource.CreationDateTime,
                    LastUpdateDateTime = res.Resource.LastUpdateDateTime,
                    DestinationUrl = res.Resource.DestinationUrl
                };
            }
            return null;
        }
    }
}
