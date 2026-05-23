-- Optional manual run: backfill public."Leads"."CompanyName" and ProductId for dashboard scope.
-- Prefer API startup (PublicTenantDataConsolidationService) which runs the same logic automatically.

-- 1) From legacy tenant schemas (replace tenant_example with your schema name)
-- UPDATE public."Leads" AS p
-- SET
--     "CompanyName" = COALESCE(NULLIF(TRIM(t."CompanyName"), ''), 'Your Company Name'),
--     "ProductId" = COALESCE(p."ProductId", t."ProductId", 1)
-- FROM tenant_example."Leads" AS t
-- WHERE LOWER(TRIM(p."Email")) = LOWER(TRIM(t."Email"))
--   AND (p."CompanyName" IS NULL OR TRIM(p."CompanyName") = '');

-- 2) From website-demo event metadata
UPDATE public."Leads" AS l
SET "CompanyName" = inferred.company_name
FROM (
    SELECT DISTINCT ON (e."LeadId")
        e."LeadId",
        NULLIF(TRIM(e."MetadataJson"::json ->> 'companyName'), '') AS company_name
    FROM public."Events" AS e
    WHERE e."LeadId" IS NOT NULL
      AND e."MetadataJson" IS NOT NULL
      AND e."MetadataJson"::json ? 'companyName'
    ORDER BY e."LeadId", e."TimestampUtc" DESC
) AS inferred
WHERE l."Id" = inferred."LeadId"
  AND inferred.company_name IS NOT NULL
  AND (l."CompanyName" IS NULL OR TRIM(l."CompanyName") = '');

-- 3) Default ProductId for scoped dashboard leads
UPDATE public."Leads"
SET "ProductId" = 1
WHERE "ProductId" IS NULL
  AND "CompanyName" IS NOT NULL
  AND TRIM("CompanyName") <> '';
