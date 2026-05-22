using System.Text;
using BoardGames.Data;
using BoardGames.Hubs.Avalon;
using BoardGames.Hubs.BlackJack;
using BoardGames.Services;
using BoardGames.Services.Avalon;
using BoardGames.Services.BlackJack;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>                                              
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IGameBalanceRepository, GameBalanceRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();                                            
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddSingleton<IBlackJackRoomManager, BlackJackRoomManager>();
builder.Services.AddSingleton<IAvalonRoomManager, AvalonRoomManager>();
builder.Services.AddSingleton<ITurnTimerService, TurnTimerService>();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>                                                                        
    {                                                     
        options.TokenValidationParameters = new TokenValidationParameters
        {                                                                                           
            ValidateIssuer = true,
            ValidateAudience = true,                                                                
            ValidateLifetime = true,                      
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Issuer"],                                    
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

// Serve frontend static files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<BlackJackHub>("/hub/blackjack");
app.MapHub<AvalonHub>("/hub/avalon");

// SPA fallback: serve index.html for non-API/hub routes
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
