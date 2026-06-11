using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.Web.Sockets;
using Sdk.Socket.WebSample;

var plugBuilder = new WebAdapterBuilder();

await plugBuilder.RunAsync(args, builder =>
    {
        builder.Services.AddSingleton<IAdapterService, WebDemoAdapterService>();

        builder.Services.AddDataPipeline();

        // Add services to the container.
        builder.Services.AddControllers();
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();
    },
    app =>
    {
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
        
        
        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();
        
    });




