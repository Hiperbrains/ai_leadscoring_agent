using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadScoring.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProductScopedEmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailTemplates_Stage",
                table: "EmailTemplates");

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "EmailTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Stage_ProductId",
                table: "EmailTemplates",
                columns: new[] { "Stage", "ProductId" },
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.Sql(
                """
                INSERT INTO public."EmailTemplates" ("Name", "ProductId", "Stage", "Subject", "EmailBodyHtml", "CtaButtonText", "CtaLink", "IsTrackingEnabled", "IsActive", "CreatedAt", "UpdatedAt")
                SELECT
                    v."Name",
                    v."ProductId",
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
                        'Welcome - Cold Stage (Product 1)',
                        1,
                        0,
                        'Your hiring pipeline is leaking top talent',
                        $cold$<!DOCTYPE html>
                <html>
                <body style="margin:0;padding:0;background-color:#021f33;font-family:Arial,sans-serif;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#021f33;padding:24px 0;">
                    <tr>
                      <td align="center">
                        <table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;background-color:#032844;color:#ffffff;padding:28px;border-radius:8px;">
                          <tr><td style="font-size:34px;font-weight:700;color:#6af47c;padding-bottom:10px;">HiperBrains</td></tr>
                          <tr><td style="font-size:38px;line-height:1.2;font-weight:700;padding-bottom:12px;">Your hiring pipeline is leaking top talent</td></tr>
                          <tr><td style="font-size:18px;line-height:1.5;color:#d8f3ff;padding-bottom:22px;">While you are manually screening resumes, your best candidates are already interviewing elsewhere. Faster hiring systems consistently win top talent.</td></tr>
                          <tr><td style="font-size:17px;line-height:1.5;padding-bottom:20px;">HiperBrains automates screening, scheduling, interviews, and evaluation so your team can focus on decisions instead of admin work.</td></tr>
                          <tr><td style="font-size:12px;line-height:1.5;color:#9ec9da;">HiperBrains, 4100 Spring Valley Road, Suite 710, Farmers Branch, Dallas, TX 75244, United States</td></tr>
                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>$cold$,
                        'VIEW ENTERPRISE PRICING',
                        'https://www.hiperbrains.com/enterprise?event={{event}}_cold_primary&email={{email}}&leadId={{leadId}}',
                        true,
                        true
                    ),
                    (
                        'Welcome - Warm Stage (Product 1)',
                        1,
                        1,
                        'Explore real hiring use cases for your industry',
                        $warm$<!DOCTYPE html>
                <html>
                <body style="margin:0;padding:0;background-color:#021f33;font-family:Arial,sans-serif;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#021f33;padding:24px 0;">
                    <tr>
                      <td align="center">
                        <table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;background-color:#032844;color:#ffffff;padding:28px;border-radius:8px;">
                          <tr><td style="font-size:34px;font-weight:700;color:#6af47c;padding-bottom:10px;">HiperBrains</td></tr>
                          <tr><td style="font-size:38px;line-height:1.2;font-weight:700;padding-bottom:12px;">Explore real hiring use cases for your industry</td></tr>
                          <tr><td style="font-size:18px;line-height:1.5;color:#d8f3ff;padding-bottom:22px;">You have already shown interest in pricing, events, blogs, and platform pages. Now see how HiperBrains solves hiring for technology and SaaS teams in real workflows.</td></tr>
                          <tr><td style="font-size:17px;line-height:1.5;padding-bottom:20px;">Compare outcomes, understand role-based workflows, and discover how teams move from sourcing to offer decisions with less effort.</td></tr>
                          <tr><td style="font-size:12px;line-height:1.5;color:#9ec9da;">HiperBrains, 4100 Spring Valley Road, Suite 710, Farmers Branch, Dallas, TX 75244, United States</td></tr>
                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>$warm$,
                        'EXPLORE USE CASES',
                        'https://www.hiperbrains.com/use-cases/technology-saas?event={{event}}_warm_primary&email={{email}}&leadId={{leadId}}',
                        true,
                        true
                    ),
                    (
                        'Welcome - MQL Stage (Product 1)',
                        1,
                        2,
                        'See the full HiperBrains hiring platform in action',
                        $mql$<!DOCTYPE html>
                <html>
                <body style="margin:0;padding:0;background-color:#021f33;font-family:Arial,sans-serif;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#021f33;padding:24px 0;">
                    <tr>
                      <td align="center">
                        <table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;background-color:#032844;color:#ffffff;padding:28px;border-radius:8px;">
                          <tr><td style="font-size:34px;font-weight:700;color:#6af47c;padding-bottom:10px;">HiperBrains</td></tr>
                          <tr><td style="font-size:38px;line-height:1.2;font-weight:700;padding-bottom:12px;">See the full HiperBrains hiring platform in action</td></tr>
                          <tr><td style="font-size:18px;line-height:1.5;color:#d8f3ff;padding-bottom:22px;">At this stage, the fastest way to decide is a personalized walkthrough. We will map your hiring flow, role types, and current bottlenecks.</td></tr>
                          <tr><td style="font-size:17px;line-height:1.5;padding-bottom:20px;">Get answers on security, process design, integrations, and deployment expectations for your team size.</td></tr>
                          <tr><td style="font-size:12px;line-height:1.5;color:#9ec9da;">HiperBrains, 4100 Spring Valley Road, Suite 710, Farmers Branch, Dallas, TX 75244, United States</td></tr>
                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>$mql$,
                        'BOOK A PERSONALIZED DEMO',
                        'https://www.hiperbrains.com/enterprise?event={{event}}_mql_primary&email={{email}}&leadId={{leadId}}',
                        true,
                        true
                    ),
                    (
                        'Welcome - Hot Stage (Product 1)',
                        1,
                        3,
                        'Final invite: launch your hiring platform today',
                        $hot$<!DOCTYPE html>
                <html>
                <body style="margin:0;padding:0;background-color:#021f33;font-family:Arial,sans-serif;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#021f33;padding:24px 0;">
                    <tr>
                      <td align="center">
                        <table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;background-color:#032844;color:#ffffff;padding:28px;border-radius:8px;">
                          <tr><td style="font-size:34px;font-weight:700;color:#6af47c;padding-bottom:10px;">HiperBrains</td></tr>
                          <tr><td style="font-size:38px;line-height:1.2;font-weight:700;padding-bottom:12px;">Final invite: launch your hiring platform today</td></tr>
                          <tr><td style="font-size:18px;line-height:1.5;color:#d8f3ff;padding-bottom:22px;">You have already visited pricing, events, blog, and use-case pages. Join now and turn hiring speed into a measurable business advantage.</td></tr>
                          <tr><td style="font-size:17px;line-height:1.5;padding-bottom:20px;">Set up your workspace, define interview flows, and start hiring with AI-supported decisions from day one.</td></tr>
                          <tr><td style="font-size:12px;line-height:1.5;color:#9ec9da;">HiperBrains, 4100 Spring Valley Road, Suite 710, Farmers Branch, Dallas, TX 75244, United States</td></tr>
                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>$hot$,
                        'SIGN UP & JOIN HIPERBRAINS',
                        'https://www.hiperbrains.com/?event={{event}}_hot_primary&email={{email}}&leadId={{leadId}}',
                        true,
                        true
                    )
                ) AS v("Name", "ProductId", "Stage", "Subject", "EmailBodyHtml", "CtaButtonText", "CtaLink", "IsTrackingEnabled", "IsActive")
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM public."EmailTemplates" t
                    WHERE t."ProductId" = v."ProductId"
                      AND t."Stage" = v."Stage"
                      AND t."IsActive" = true
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailTemplates_Stage_ProductId",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "EmailTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Stage",
                table: "EmailTemplates",
                column: "Stage",
                unique: true,
                filter: "\"IsActive\" = true");
        }
    }
}
