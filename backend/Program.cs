using Backend.Extensions;
using Backend.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices(builder);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

GeneralEndpoints.MapRoutes(app, app.Environment);
TemplateCacheEndpoints.MapRoutes(app);

app.Run();
