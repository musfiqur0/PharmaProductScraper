using System.Text.Json;
using Dapper;
using Npgsql;
using PharmaProductScraper.Models;

namespace PharmaProductScraper.Repositories;


public sealed class ProductRepository
{
    private readonly string _connectionString;

    public ProductRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<Product>> GetProductsAsync(int take)
    {
        const string sql = """
            SELECT
                p.id,
                p.name,
                p.generic_name AS GenericName,
                p.strength
            FROM public.product p
            WHERE is_deleted = false
              AND is_active = true
              --AND (
              --      monograph IS NULL
              --      OR monograph = '{}'::jsonb
              --      OR product_identifier IS NULL
              --    )
            ORDER BY p.id Desc
            LIMIT @Take;
            """;

        await using var connection =
            new NpgsqlConnection(_connectionString);

        var products = await connection.QueryAsync<Product>(
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
                --"name",
                --barcode,
                generic_name,
                --"type",
                --origin,
                --category,
                --sub_category,
                --dose,
                image_name,
                url,
                --image_uploaded_by_user_id,
                --is_prescription_required,
                --is_strip_allowed,
                --medicine_per_strips,
                --cost_per_unit,
                --vat,
                --new_cost_per_unit,
                rate_per_unit,
                --is_approved,
                --is_active,
                --is_deleted,
                --deleted_by_crm_user_id,
                --deleted_by_crm_user_at,
                --created_by_pharmacy_user_id,
                --processed_by_crm_user_id,
                --processed_by_crm_user_at,
                --created_by_crm_user_id,
                --created_at,
                updated_at,
                --pharmacy_supplier_id,
                --"size",
                --tp_per_unit,
                --vat_per_unit,
                pack_size,
                strength,
                --prescription_note,
                --product_details,
                --created_by_supplier_user_id,
                --discontinued_at,
                --discontinued_by_supplier_user_id,
                product_identifier,
                --therapeutic_medicine_class_type,
                monograph,
                is_add_lookup_drug
            )
            SELECT
                p.id,
                --p."name",
                --p.barcode,

                COALESCE(
                    NULLIF(@GenericName, ''),
                    p.generic_name
                ),

                --p."type",
                --p.origin,
                --p.category,
                --p.sub_category,
                --p.dose,

                COALESCE(
                    NULLIF(@ImageUrl, ''),
                    p.image_name
                ),

                COALESCE(
                    NULLIF(@ProductUrl, ''),
                    p.url
                ),

                --p.image_uploaded_by_user_id,
                --p.is_prescription_required,
                --p.is_strip_allowed,
                --p.medicine_per_strips,
                --p.cost_per_unit,
                --p.vat,
                --p.new_cost_per_unit,

                COALESCE(
                    @Price,
                    p.rate_per_unit
                ),

                --p.is_approved,
                --p.is_active,
                --p.is_deleted,
                --p.deleted_by_crm_user_id,
                --p.deleted_by_crm_user_at,
                --p.created_by_pharmacy_user_id,
                --p.processed_by_crm_user_id,
                --p.processed_by_crm_user_at,
                --p.created_by_crm_user_id,
                --p.created_at,

                NOW(),

                --p.pharmacy_supplier_id,
                --p."size",
                --p.tp_per_unit,
                --p.vat_per_unit,

                COALESCE(
                    @PackSize,
                    p.pack_size
                ),

                COALESCE(
                    NULLIF(@Strength, ''),
                    p.strength
                ),

                --p.prescription_note,
                --p.product_details,
                --p.created_by_supplier_user_id,
                --p.discontinued_at,
                --p.discontinued_by_supplier_user_id,

                COALESCE(
                    NULLIF(@ExternalId, ''),
                    p.product_identifier
                ),

                --p.therapeutic_medicine_class_type,

                CASE
                    WHEN @MonographJson IS NULL
                        THEN p.monograph
                    ELSE CAST(@MonographJson AS jsonb)
                END,

                true

            FROM public.product p
            WHERE p.id = @ProductId

            ON CONFLICT (id)
            DO UPDATE SET
                generic_name = EXCLUDED.generic_name,
                strength = EXCLUDED.strength,
                url = EXCLUDED.url,
                image_name = EXCLUDED.image_name,
                rate_per_unit = EXCLUDED.rate_per_unit,
                pack_size = EXCLUDED.pack_size,
                product_identifier = EXCLUDED.product_identifier,
                monograph = EXCLUDED.monograph,
                is_add_lookup_drug = EXCLUDED.is_add_lookup_drug,
                updated_at = NOW();
            """;

        var monographJson =
            result.Monograph.Count > 0
                ? JsonSerializer.Serialize(result.Monograph)
                : null;

        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.ExecuteAsync(
            sql,
            new
            {
                ProductId = productId,

                result.GenericName,
                result.Strength,
                result.ProductUrl,
                result.ImageUrl,
                result.Price,
                result.PackSize,
                result.ExternalId,

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