using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Referral;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

// Public read-only endpoint for the checkout "Referred By" dropdown.
// Admin CRUD lives in RioCommerce.Web/Components/Pages/Admin/Promotions/ReferredBy*.razor and uses the
// IReferralAdminService directly via DI.
[ApiController]
[Route("api/referrals")]
public class ReferralsController : ControllerBase
{
    private readonly IReferralService _svc;
    public ReferralsController(IReferralService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ReferralSourceOption>>>> List()
        => Ok(ApiResponse<List<ReferralSourceOption>>.Ok(await _svc.ListActiveAsync()));
}
