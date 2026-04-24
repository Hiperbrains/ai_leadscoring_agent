namespace LeadScoring.Api.Services;

public static class FirstTimeLeadEmailTemplate
{
    public const string Subject = "Your hiring pipeline is leaking top talent";

    public static string BuildHtml(string recipientEmail, string eventName)
    {
        var encodedEmail = Uri.EscapeDataString(recipientEmail);
        var encodedEvent = Uri.EscapeDataString(eventName);

        return $$"""
            <!DOCTYPE html>
            <html>
            <body style="margin:0;padding:0;background-color:#021f33;font-family:Arial,sans-serif;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#021f33;padding:24px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;background-color:#032844;color:#ffffff;padding:28px;border-radius:8px;">
                      <tr>
                        <td style="font-size:34px;font-weight:700;color:#6af47c;padding-bottom:10px;">HiperBrains</td>
                      </tr>
                      <tr>
                        <td style="font-size:38px;line-height:1.2;font-weight:700;padding-bottom:12px;">Your hiring pipeline is leaking top talent</td>
                      </tr>
                      <tr>
                        <td style="font-size:18px;line-height:1.5;color:#d8f3ff;padding-bottom:22px;">While you're manually screening resumes, your best candidates are quietly interviewing elsewhere and by the time you're ready to move, they've already signed with someone faster.</td>
                      </tr>
                      <tr>
                        <td>
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin-bottom:20px;">
                            <tr>
                              <td width="33%" style="font-size:30px;font-weight:700;color:#ffffff;">40%</td>
                              <td width="33%" style="font-size:30px;font-weight:700;color:#ffffff;">25%</td>
                              <td width="33%" style="font-size:30px;font-weight:700;color:#ffffff;">1 Platform</td>
                            </tr>
                            <tr>
                              <td style="font-size:14px;color:#35e26f;">Faster time to hire</td>
                              <td style="font-size:14px;color:#35e26f;">Drop in turnover</td>
                              <td style="font-size:14px;color:#35e26f;">End to end</td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td align="center" style="padding-bottom:20px;">
                          <a href="https://www.hiperbrains.com/book-demo/?event={{encodedEvent}}_primary&email={{encodedEmail}}" style="display:inline-block;background:#2de06a;color:#00233c;text-decoration:none;font-weight:700;padding:14px 34px;border-radius:8px;">BOOK A DEMO</a>
                        </td>
                      </tr>
                      <tr>
                        <td style="font-size:17px;line-height:1.5;padding-bottom:20px;">HiperBrains automates every step - screening, scheduling, and decisions - so you hire faster and smarter.</td>
                      </tr>
                      <tr>
                        <td align="center" style="padding-bottom:18px;">
                          <table role="presentation" cellspacing="0" cellpadding="0">
                            <tr>
                              <td style="background:#06b77c;border-radius:18px;padding:8px 14px;color:#ffffff;font-size:13px;">IVR pre-screening</td>
                              <td style="width:8px;"></td>
                              <td style="background:#06b77c;border-radius:18px;padding:8px 14px;color:#ffffff;font-size:13px;">Multi-language support</td>
                              <td style="width:8px;"></td>
                              <td style="background:#06b77c;border-radius:18px;padding:8px 14px;color:#ffffff;font-size:13px;">Resume Parser &amp; Matching</td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td align="center" style="font-size:18px;font-weight:700;padding-bottom:10px;">Most teams who use HiperBrains live say the same thing:<br/>"Why weren't we doing this already?"</td>
                      </tr>
                      <tr>
                        <td align="center" style="padding-bottom:20px;">
                          <a href="https://www.hiperbrains.com/book-demo/?event={{encodedEvent}}_secondary&email={{encodedEmail}}" style="display:inline-block;background:#ffffff;color:#01273f;text-decoration:none;font-weight:700;padding:14px 28px;border-radius:8px;">BOOK YOUR FREE DEMO</a>
                        </td>
                      </tr>
                      <tr>
                        <td style="font-size:12px;line-height:1.5;color:#9ec9da;">HiperBrains, 4100 Spring Valley Road, Suite 710, Farmers Branch, Dallas, TX 75244, United States</td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
