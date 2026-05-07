using BoardGames.Dtos;
using BoardGames.Services;
using Microsoft.AspNetCore.Mvc;

namespace BoardGames.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IJwtService jwtService, IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequestDto registerRequestDto)
    {
        var username = registerRequestDto.Username;
        var password = registerRequestDto.Password;
        if (await authService.UsernameExists(username))
            return BadRequest("Username already exists");
        await authService.CreateUser(username, password);
        return Ok(new { message = "Registered successfully" });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequestDto loginRequestDto)
    {
        var user = await authService.ValidateUser(loginRequestDto.Username, loginRequestDto.Password);
        if (user == null)
        {
            return Unauthorized("Username or password is incorrect");
        }

        var token = jwtService.GenerateJwtToken(user);
        return Ok(new { token });
    }

}