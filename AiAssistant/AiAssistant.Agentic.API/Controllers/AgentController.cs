using AiAssistant.Agentic.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiAssistant.Agentic.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AgentController : ControllerBase
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly ILogger<AgentController> _logger;

    public AgentController(AgentOrchestrator orchestrator, ILogger<AgentController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AgentAskRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Question))
            return BadRequest("Question is required.");

        _logger.LogInformation("Agent ask: '{Question}'", request.Question);

        var result = await _orchestrator.RunAsync(request.Question, ct);
        return Ok(result);
    }
}

public class AgentAskRequest
{
    public string Question { get; set; } = "";
}
