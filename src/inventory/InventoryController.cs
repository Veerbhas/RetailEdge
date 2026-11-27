using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace RetailEdge.Inventory;

[ApiController]
[Route("inventory")]
[Authorize]
public sealed class InventoryController(IInventoryService inventoryService)
    : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateInventoryRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthorizedStore(out Guid storeId))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = new
                    {
                        code = "STORE_ACCESS_DENIED",
                        message = "Store access was denied."
                    }
                });
        }

        try
        {
            InventoryResponse response =
                await inventoryService.CreateAsync(
                    storeId,
                    request,
                    cancellationToken);
            return CreatedAtAction(
                nameof(Get),
                new { productId = response.ProductId },
                response);
        }
        catch (InventoryAlreadyExistsException)
        {
            return Conflict(new
            {
                error = new
                {
                    code = "INVENTORY_ALREADY_EXISTS",
                    message = "The store already has inventory for this product."
                }
            });
        }
    }

    [HttpGet("{productId}")]
    public async Task<IActionResult> Get(
        string productId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthorizedStore(out Guid storeId))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = new
                    {
                        code = "STORE_ACCESS_DENIED",
                        message = "Store access was denied."
                    }
                });
        }

        InventoryResponse? response =
            await inventoryService.GetByProductAsync(
                storeId,
                productId,
                cancellationToken);
        return response is null
            ? NotFound(new
            {
                error = new
                {
                    code = "INVENTORY_NOT_FOUND",
                    message = "Inventory was not found."
                }
            })
            : Ok(response);
    }

    [HttpPut("{productId}")]
    public async Task<IActionResult> Update(
        string productId,
        [FromBody] UpdateInventoryStockRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthorizedStore(out Guid storeId))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = new
                    {
                        code = "STORE_ACCESS_DENIED",
                        message = "Store access was denied."
                    }
                });
        }

        try
        {
            InventoryResponse response =
                await inventoryService.UpdateStockAsync(
                    storeId,
                    productId,
                    request,
                    cancellationToken);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                error = new
                {
                    code = "INVENTORY_NOT_FOUND",
                    message = "Inventory was not found."
                }
            });
        }
        catch (InventoryConcurrencyException)
        {
            return Conflict(new
            {
                error = new
                {
                    code = "CONCURRENT_MODIFICATION",
                    message = "The inventory changed since it was read."
                }
            });
        }
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> Delete(
        string productId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthorizedStore(out Guid storeId))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = new
                    {
                        code = "STORE_ACCESS_DENIED",
                        message = "Store access was denied."
                    }
                });
        }

        bool deleted =
            await inventoryService.DeleteAsync(
                storeId,
                productId,
                cancellationToken);
        return deleted
            ? NoContent()
            : NotFound(new
            {
                error = new
                {
                    code = "INVENTORY_NOT_FOUND",
                    message = "Inventory was not found."
                }
            });
    }

    private bool TryGetAuthorizedStore(out Guid storeId)
    {
        string? claim = User.FindFirst("store_id")?.Value
            ?? User.FindFirst("storeId")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out storeId)
            && storeId != Guid.Empty;
    }
}
