using System.Text.Json;
using ClaimsManagment.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsManagment.Controllers;

/// <summary>Provides CRUD endpoints for the dbo.POLLICT table.</summary>
[ApiController]
[Route("api/pollict")]
public sealed class PollictController(IPollictService service) : ControllerBase
{
    /// <summary>Returns all POLLICT rows.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<IReadOnlyDictionary<string, object?>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> GetAll(
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(cancellationToken));
    }

    /// <summary>Returns a POLLICT row by its primary key.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyDictionary<string, object?>>> GetById(
        string id,
        CancellationToken cancellationToken)
    {
        var row = await service.GetByIdAsync(id, cancellationToken);
        return row is null ? NotFound() : Ok(row);
    }

    /// <summary>Creates a POLLICT row using the supplied table column values.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, object?>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyDictionary<string, object?>>> Create(
        [FromBody] Dictionary<string, JsonElement> values,
        CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(values, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.PrimaryKeyValue }, created.Row);
    }

    /// <summary>Updates the supplied POLLICT columns for a row.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyDictionary<string, object?>>> Update(
        string id,
        [FromBody] Dictionary<string, JsonElement> values,
        CancellationToken cancellationToken)
    {
        var row = await service.UpdateAsync(id, values, cancellationToken);
        return row is null ? NotFound() : Ok(row);
    }
}
