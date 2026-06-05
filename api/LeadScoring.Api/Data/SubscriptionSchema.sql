-- Company subscription & payment tables (public schema).
-- Applied via EF migration AddCompanySubscriptionsAndPayments.

CREATE TABLE IF NOT EXISTS public."Subscriptions" (
    "SubscriptionId" uuid NOT NULL PRIMARY KEY,
    "CompanyId" uuid NOT NULL REFERENCES public."Tenants" ("Id") ON DELETE RESTRICT,
    "CurrentPlan" text NOT NULL,
    "BillingType" text NOT NULL,
    "StartDate" timestamptz NOT NULL,
    "RenewDate" timestamptz NULL,
    "ExpireDate" timestamptz NULL,
    "LastPaymentAmount" bigint NULL,
    "StripeCustomerId" text NULL,
    "StripeSubscriptionId" text NULL,
    "PaymentStatus" text NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    "CreatedDate" timestamptz NOT NULL DEFAULT now(),
    "UpdatedDate" timestamptz NULL,
    "ProjectCreditsTotal" integer NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS "IX_Subscriptions_CompanyId"
    ON public."Subscriptions" ("CompanyId");

CREATE INDEX IF NOT EXISTS "IX_Subscriptions_IsActive"
    ON public."Subscriptions" ("IsActive");

CREATE UNIQUE INDEX IF NOT EXISTS "UX_Subscriptions_Company_Active"
    ON public."Subscriptions" ("CompanyId")
    WHERE "IsActive" = true;

CREATE TABLE IF NOT EXISTS public."Payments" (
    "PaymentId" uuid NOT NULL PRIMARY KEY,
    "CompanyId" uuid NOT NULL REFERENCES public."Tenants" ("Id") ON DELETE RESTRICT,
    "SubscriptionId" uuid NOT NULL REFERENCES public."Subscriptions" ("SubscriptionId") ON DELETE RESTRICT,
    "StripeSessionId" text NULL,
    "StripePaymentIntentId" text NULL,
    "Amount" bigint NOT NULL,
    "Currency" varchar(10) NOT NULL DEFAULT 'usd',
    "PaymentStatus" text NOT NULL,
    "PaymentDate" timestamptz NOT NULL,
    "InvoiceUrl" text NULL,
    "CreatedDate" timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS "IX_Payments_CompanyId"
    ON public."Payments" ("CompanyId");

CREATE INDEX IF NOT EXISTS "IX_Payments_SubscriptionId"
    ON public."Payments" ("SubscriptionId");

CREATE INDEX IF NOT EXISTS "IX_Payments_PaymentDate"
    ON public."Payments" ("PaymentDate");
