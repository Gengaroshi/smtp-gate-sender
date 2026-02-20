using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SmtpGateSender.Swagger;

public sealed class EmailExamplesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.ApiDescription.HttpMethod ?? "";
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return;

        var path = (context.ApiDescription.RelativePath ?? "").Trim().TrimStart('/');

        // Tylko dla /email i /api/email
        if (!path.Equals("email", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("api/email", StringComparison.OrdinalIgnoreCase))
            return;

        operation.RequestBody ??= new OpenApiRequestBody();
        operation.RequestBody.Content["application/json"] = new OpenApiMediaType
        {
            Examples = new Dictionary<string, OpenApiExample>
            {
                ["HtmlDemo"] = new OpenApiExample
                {
                    Summary = "HTML email (demo)",
                    Value = new OpenApiObject
                    {
                        ["subject"] = new OpenApiString("SMTP Gate Sender test (HTML)"),
                        ["body"] = new OpenApiString("<b>HTML test z gotowego szablonu</b>"),
                        ["isHtml"] = new OpenApiBoolean(true),
                        ["toEmails"] = new OpenApiArray { new OpenApiString("test@homelab.local") },
                        ["ccEmails"] = new OpenApiArray(),
                        ["from"] = new OpenApiString("sender@homelab.local"),
                        ["requestId"] = new OpenApiString("demo-html-001"),
                        ["client"] = new OpenApiString("swagger")
                    }
                },
                ["PlainTextDemo"] = new OpenApiExample
                {
                    Summary = "Plain text email (demo)",
                    Value = new OpenApiObject
                    {
                        ["subject"] = new OpenApiString("SMTP Gate Sender test (TXT)"),
                        ["body"] = new OpenApiString("To jest test tekstowy"),
                        ["isHtml"] = new OpenApiBoolean(false),
                        ["toEmails"] = new OpenApiArray { new OpenApiString("test@homelab.local") },
                        ["ccEmails"] = new OpenApiArray(),
                        ["from"] = new OpenApiString("sender@homelab.local"),
                        ["requestId"] = new OpenApiString("demo-txt-001"),
                        ["client"] = new OpenApiString("swagger")
                    }
                },
                ["InvalidEmail"] = new OpenApiExample
                {
                    Summary = "B³êdny email (400)",
                    Value = new OpenApiObject
                    {
                        ["subject"] = new OpenApiString("Bad email"),
                        ["body"] = new OpenApiString("Ma zwróciæ 400"),
                        ["isHtml"] = new OpenApiBoolean(false),
                        ["toEmails"] = new OpenApiArray { new OpenApiString("string") },
                        ["ccEmails"] = new OpenApiArray(),
                        ["from"] = new OpenApiString("sender@homelab.local"),
                        ["requestId"] = new OpenApiString("demo-bad-001"),
                        ["client"] = new OpenApiString("swagger")
                    }
                }
            }
        };
    }
}
