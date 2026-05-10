using System.Text;
using BoardGames.Data;
using BoardGames.Hubs.BlackJack;
using BoardGames.Services;
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
builder.Services.AddScoped<IAuthService, AuthService>();                                            
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddSingleton<IBlackJackRoomManager, BlackJackRoomManager>();
builder.Services.AddSignalR();
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
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))                          
        };                                                
    });


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<BlackJackHub>("/hub/blackjack");
app.Run();
