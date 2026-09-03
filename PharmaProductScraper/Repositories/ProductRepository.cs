using System.Text.Json;
using Dapper;
using Npgsql;
using PharmaProductScraper.Models;

namespace PharmaProductScraper.Repositories;


public sealed class ProductRepository
{
    private readonly string _connectionString;
    private readonly string _connectionStringLive;

    public ProductRepository(string connectionString, string connectionStringLive)
    {
        _connectionString = connectionString;
        _connectionStringLive = connectionStringLive;
    }

    public async Task<List<Product>> GetProductsAsync(int take)
    {
        const string sql = """
            SELECT
                p.id,
                p.name,
                p.generic_name AS GenericName,
                p.strength,
                p.category as Form
            FROM public.product p
            WHERE is_deleted = false
              AND is_active = true
              AND type = 'PHARMACEUTICAL'
              --AND (
              --      monograph IS NULL
              --      OR monograph = '{}'::jsonb
              --      OR product_identifier IS NULL
              --    )
            ORDER BY p.id Desc
            LIMIT @Take;
            """;

        await using var connectionLive = new NpgsqlConnection(_connectionStringLive);

        var products = await connectionLive.QueryAsync<Product>(
            sql,
            new
            {
                Take = take
            });

        return products.ToList();
    }



    public async Task UpdateProductAsync(
    long productId,
    ScrapedProduct result)
    {
        const string sql = """
        INSERT INTO public.productupdated
        (
            id,
            name,
            generic_name,
            "type",
            category,
            url,
            is_prescription_required,
            medicine_per_strips,
            rate_per_unit,
            pack_size,
            "size",
            strength,
            monograph,
            --is_add_lookup_drug,
            updated_at
        )
        VALUES
        (
            @ProductId,
            NULLIF(@Name, ''),
            NULLIF(@GenericName, ''),
            NULLIF(@Type, ''),
            NULLIF(@Category, ''),
            NULLIF(@ProductUrl, ''),
            @IsPrescriptionRequired,
            @MedicinePerStrips,
            @Price,
            @PackSize,
            NULLIF(@Size, ''),
            NULLIF(@Strength, ''),
            CAST(@MonographJson AS jsonb),
            --true,
            NOW()
        )

        ON CONFLICT (id)
        DO UPDATE SET
            name = EXCLUDED.name,
            generic_name = EXCLUDED.generic_name,
            "type" = EXCLUDED."type",
            category = EXCLUDED.category,
            url = EXCLUDED.url,
            is_prescription_required = EXCLUDED.is_prescription_required,
            medicine_per_strips = EXCLUDED.medicine_per_strips,
            rate_per_unit = EXCLUDED.rate_per_unit,
            pack_size = EXCLUDED.pack_size,
            "size" = EXCLUDED."size",
            strength = EXCLUDED.strength,
            monograph = EXCLUDED.monograph,
            --is_add_lookup_drug = true,
            updated_at = NOW();
        """;

        var monographJson =
            result.Monograph.Count > 0
                ? JsonSerializer.Serialize(
                    result.Monograph)
                : "{}";

        await using var connection =
            new NpgsqlConnection(
                _connectionString);

        await connection.ExecuteAsync(
            sql,
            new
            {
                ProductId = productId,

                result.Name,
                result.GenericName,
                result.Type,
                result.Category,
                result.ProductUrl,
                result.IsPrescriptionRequired,
                result.MedicinePerStrips,
                result.Price,
                result.PackSize,
                result.Size,
                result.Strength,

                MonographJson = monographJson
            });
    }


    //public async Task UpdateProductAsync(
    //    long productId,
    //    ScrapedProduct result)
    //{
    //    const string sql = """
    //        UPDATE public.product
    //        SET
    //            generic_name = COALESCE(NULLIF(@GenericName, ''), generic_name),
    //            strength = COALESCE(NULLIF(@Strength, ''), strength),
    //            url = COALESCE(NULLIF(@ProductUrl, ''), url),
    //            image_name = COALESCE(NULLIF(@ImageUrl, ''), image_name),
    //            rate_per_unit = COALESCE(@Price, rate_per_unit),
    //            pack_size = COALESCE(@PackSize, pack_size),
    //            product_identifier = COALESCE(NULLIF(@ExternalId, ''), product_identifier),
    //            monograph =
    //                CASE
    //                    WHEN @MonographJson IS NULL
    //                        THEN monograph

    //                    ELSE CAST(@MonographJson AS jsonb)
    //                END,

    //            updated_at = NOW()

    //        WHERE id = @ProductId;
    //        """;

    //    var monographJson =
    //        result.Monograph.Count > 0
    //            ? JsonSerializer.Serialize(result.Monograph)
    //            : null;

    //    await using var connection =
    //        new NpgsqlConnection(_connectionString);

    //    await connection.ExecuteAsync(
    //        sql,
    //        new
    //        {
    //            ProductId = productId,

    //            result.GenericName,
    //            result.Strength,
    //            result.ProductUrl,
    //            result.ImageUrl,
    //            result.Price,
    //            result.PackSize,
    //            result.ExternalId,

    //            MonographJson = monographJson
    //        });
    //}
}