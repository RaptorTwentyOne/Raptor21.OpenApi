using Raptor21.OpenApi.Generics.AspNetCore;
using Raptor21.OpenApi.Generics.Sample.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSwaggerGen(c =>
{
    c.AddOpenApiGenerics(g => g
        // The envelope lives in a shared package, so it is registered rather than annotated. UseEnvelope also
        // applies it to responses that declare only their payload.
        .UseEnvelope(typeof(BaseResponse<>))
        .AddContainer(typeof(Page<>)));
});

var app = builder.Build();

app.UseSwagger();
app.MapControllers();

app.Run();
