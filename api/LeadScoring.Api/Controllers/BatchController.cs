using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("batch")]
public class BatchController(IBatchProcessingService batchProcessingService, ILogger<BatchController> logger) : ControllerBase
{
    [HttpPost("retry/{batchId:long}")]
    public async Task<IActionResult> Retry(long batchId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await batchProcessingService.RetryFailedLeadsAsync(batchId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Retry failed for batch {BatchId}", batchId);
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected retry failure for batch {BatchId}", batchId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Retry failed due to internal error." });
        }
    }
}
