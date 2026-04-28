using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowUpEmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailTemplates_Stage_ProductId",
                table: "EmailTemplates");

            migrationBuilder.AddColumn<bool>(
                name: "IsFollowUp",
                table: "EmailTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Stage_ProductId_IsFollowUp",
                table: "EmailTemplates",
                columns: new[] { "Stage", "ProductId", "IsFollowUp" },
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.Sql(
                """
                INSERT INTO public."EmailTemplates" ("Name", "ProductId", "IsFollowUp", "Stage", "Subject", "EmailBodyHtml", "CtaButtonText", "CtaLink", "IsTrackingEnabled", "IsActive", "CreatedAt", "UpdatedAt")
                SELECT
                    v."Name",
                    v."ProductId",
                    v."IsFollowUp",
                    v."Stage",
                    v."Subject",
                    v."EmailBodyHtml",
                    v."CtaButtonText",
                    v."CtaLink",
                    v."IsTrackingEnabled",
                    v."IsActive",
                    NOW(),
                    NULL
                FROM (
                    VALUES
                    (
                        'Welcome - Cold Follow-up (Product 1)',
                        1,
                        true,
                        0,
                        'Still reviewing resumes manually?',
                        $coldf$<!DOCTYPE html>
                <html>
                <body style="margin:0;padding:0;background-color:#021f33;font-family:Arial,sans-serif;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#021f33;padding:24px 0;">
                    <tr><td align="center"><table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;background-color:#032844;color:#ffffff;padding:28px;border-radius:8px;">
                      <tr><td style="font-size:34px;font-weight:700;color:#6af47c;padding-bottom:10px;">HiperBrains</td></tr>
                      <tr><td style="font-size:38px;line-height:1.2;font-weight:700;padding-bottom:12px;">Still reviewing resumes manually?</td></tr>
                      <tr><td style="font-size:18px;line-height:1.5;color:#d8f3ff;padding-bottom:22px;">Hiring speed decides outcomes. Teams that respond first secure stronger candidates and reduce offer drop-offs.</td></tr>
                      <tr><td style="font-size:17px;line-height:1.5;padding-bottom:20px;">From AI job descriptions to final evaluation, every step is connected on one platform for consistency and speed.</td></tr>
                      <tr><td style="font-size:12px;line-height:1.5;color:#9ec9da;">HiperBrains, 4100 Spring Valley Road, Suite 710, Farmers Branch, Dallas, TX 75244, United States</td></tr>
                    </table></td></tr>
                  </table>
                </body>
                </html>$coldf$,
                        'EXPLORE HOW HIPERBRAINS WORKS',
                        'https://www.hiperbrains.com/about?event={{event}}_cold_followup_primary&email={{email}}&leadId={{leadId}}',
                        true,
                        true
                    ),
                    (
                        'Welcome - Warm Follow-up (Product 1)',
                        1,
                        true,
                        1,
                        'You explored. Now launch your hiring workflow',
                        $warmf$<!DOCTYPE html>
                <html>
                <body style="margin:0;padding:0;background-color:#021f33;font-family:Arial,sans-serif;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#021f33;padding:24px 0;">
                    <tr><td align="center"><table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;background-color:#032844;color:#ffffff;padding:28px;border-radius:8px;">
                      <tr><td style="font-size:34px;font-weight:700;color:#6af47c;padding-bottom:10px;">HiperBrains</td></tr>
                      <tr><td style="font-size:38px;line-height:1.2;font-weight:700;padding-bottom:12px;">You explored. Now launch your hiring workflow.</td></tr>
                      <tr><td style="font-size:18px;line-height:1.5;color:#d8f3ff;padding-bottom:22px;">Since you have checked use cases and pricing, this is the best time to start. Set up your company and begin hiring smarter in minutes.</td></tr>
                      <tr><td style="font-size:17px;line-height:1.5;padding-bottom:20px;">Recruiters, interviewers, and hiring managers can collaborate in one place with structured, consistent scoring.</td></tr>
                      <tr><td style="font-size:12px;line-height:1.5;color:#9ec9da;">HiperBrains, 4100 Spring Valley Road, Suite 710, Farmers Branch, Dallas, TX 75244, United States</td></tr>
                    </table></td></tr>
                  </table>
                </body>
                </html>$warmf$,
                        'SIGN UP NOW',
                        'https://www.hiperbrains.com/?event={{event}}_warm_followup_primary&email={{email}}&leadId={{leadId}}',
                        true,
                        true
                    ),
                    (
                        'Welcome - MQL Follow-up (Product 1)',
                        1,
                        true,
                        2,
                        'Ready to move from evaluation to rollout?',
                        $mqlf$<!DOCTYPE html>
                <html>
                <body style="margin:0;padding:0;background-color:#021f33;font-family:Arial,sans-serif;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#021f33;padding:24px 0;">
                    <tr><td align="center"><table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;background-color:#032844;color:#ffffff;padding:28px;border-radius:8px;">
                      <tr><td style="font-size:34px;font-weight:700;color:#6af47c;padding-bottom:10px;">HiperBrains</td></tr>
                      <tr><td style="font-size:38px;line-height:1.2;font-weight:700;padding-bottom:12px;">Ready to move from evaluation to rollout?</td></tr>
                      <tr><td style="font-size:18px;line-height:1.5;color:#d8f3ff;padding-bottom:22px;">You have engaged with core pages and product intent signals. This is the right moment to activate your account and move to implementation.</td></tr>
                      <tr><td style="font-size:17px;line-height:1.5;padding-bottom:20px;">Need stakeholder buy-in first? We can align on pricing, ROI goals, and onboarding milestones with your team.</td></tr>
                      <tr><td style="font-size:12px;line-height:1.5;color:#9ec9da;">HiperBrains, 4100 Spring Valley Road, Suite 710, Farmers Branch, Dallas, TX 75244, United States</td></tr>
                    </table></td></tr>
                  </table>
                </body>
                </html>$mqlf$,
                        'START SIGNUP',
                        'https://www.hiperbrains.com/?event={{event}}_mql_followup_primary&email={{email}}&leadId={{leadId}}',
                        true,
                        true
                    ),
                    (
                        'Welcome - Hot Follow-up (Product 1)',
                        1,
                        true,
                        3,
                        'Final invite: launch your hiring platform today',
                        $hotf$<!DOCTYPE html>
                <html>
                <body style="margin:0;padding:0;background-color:#021f33;font-family:Arial,sans-serif;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#021f33;padding:24px 0;">
                    <tr><td align="center"><table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;background-color:#032844;color:#ffffff;padding:28px;border-radius:8px;">
                      <tr><td style="font-size:34px;font-weight:700;color:#6af47c;padding-bottom:10px;">HiperBrains</td></tr>
                      <tr><td style="font-size:38px;line-height:1.2;font-weight:700;padding-bottom:12px;">Final invite: launch your hiring platform today</td></tr>
                      <tr><td style="font-size:18px;line-height:1.5;color:#d8f3ff;padding-bottom:22px;">This is your final reminder to activate HiperBrains. You have done the research - now convert that intent into execution.</td></tr>
                      <tr><td style="font-size:17px;line-height:1.5;padding-bottom:20px;">If you would like a quick alignment call before starting, our team can help with setup, scope, and rollout planning.</td></tr>
                      <tr><td style="font-size:12px;line-height:1.5;color:#9ec9da;">HiperBrains, 4100 Spring Valley Road, Suite 710, Farmers Branch, Dallas, TX 75244, United States</td></tr>
                    </table></td></tr>
                  </table>
                </body>
                </html>$hotf$,
                        'CREATE ACCOUNT',
                        'https://www.hiperbrains.com/?event={{event}}_hot_followup_primary&email={{email}}&leadId={{leadId}}',
                        true,
                        true
                    )
                ) AS v("Name", "ProductId", "IsFollowUp", "Stage", "Subject", "EmailBodyHtml", "CtaButtonText", "CtaLink", "IsTrackingEnabled", "IsActive")
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM public."EmailTemplates" t
                    WHERE t."ProductId" = v."ProductId"
                      AND t."Stage" = v."Stage"
                      AND t."IsFollowUp" = v."IsFollowUp"
                      AND t."IsActive" = true
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailTemplates_Stage_ProductId_IsFollowUp",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "IsFollowUp",
                table: "EmailTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Stage_ProductId",
                table: "EmailTemplates",
                columns: new[] { "Stage", "ProductId" },
                unique: true,
                filter: "\"IsActive\" = true");
        }
    }
}
