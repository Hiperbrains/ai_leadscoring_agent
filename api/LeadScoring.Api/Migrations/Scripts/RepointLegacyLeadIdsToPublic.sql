-- One-time: point public tracking at the canonical public.Leads row (highest Score) per email.
-- Example: tenant_hiperbrains lead id vs public lead id for the same email.

WITH canonical AS (
    SELECT DISTINCT ON (LOWER(TRIM("Email")))
        "Id" AS public_lead_id,
        LOWER(TRIM("Email")) AS email_key
    FROM public."Leads"
    WHERE "Email" IS NOT NULL AND TRIM("Email") <> ''
    ORDER BY LOWER(TRIM("Email")), "Score" DESC, "CreatedAtUtc" ASC
)
UPDATE public."Events" AS e
SET "LeadId" = c.public_lead_id
FROM canonical AS c
WHERE e."LeadId" IS NOT NULL
  AND e."LeadId" <> c.public_lead_id
  AND EXISTS (
    SELECT 1
    FROM public."Leads" AS l
    WHERE l."Id" = e."LeadId"
      AND LOWER(TRIM(l."Email")) = c.email_key
  );
