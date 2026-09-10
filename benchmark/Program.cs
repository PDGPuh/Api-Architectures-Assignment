using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ApiBenchmark.Protocol;

var options = new Dictionary<string,string>();
for (int i=1;i<args.Length;i+=2) options.Add(args[i].TrimStart('-'),args[i+1]);
string Opt(string name,string fallback) => options.GetValueOrDefault(name,fallback);
var mode=args.FirstOrDefault() ?? "server";
var port=int.Parse(Opt("port","5087"));
if(mode=="server") {
    var builder=WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,port,l=>l.Protocols=HttpProtocols.Http2));
    builder.Services.AddGrpc();
    var app=builder.Build();
    app.MapGrpcService<StudentsGrpc>();
    app.MapGet("/api/students",(int limit,HttpContext context)=> {
        context.Response.Headers.CacheControl="no-store";
        return limit is <1 or >1000 ? Results.BadRequest() : Results.Json(Fixture.Json(limit),Fixture.JsonOptions);
    });
    Console.WriteLine("READY: HTTP/2 cleartext on loopback port "+port);
    await app.RunAsync();
} else if(mode=="load") {
    string protocol=Opt("protocol","rest"),output=Opt("output","result.json");
    if(protocol is not ("rest" or "grpc")) throw new ArgumentException("protocol must be rest or grpc");
    int limit=int.Parse(Opt("size","100")),concurrency=int.Parse(Opt("concurrency","1"));
    double seconds=double.Parse(Opt("seconds","5")),warmup=double.Parse(Opt("warmup","2"));
    if(limit is <1 or >1000 || concurrency<1 || seconds<=0 || warmup<0) throw new ArgumentException("Invalid benchmark parameters");
    using var handler=new SocketsHttpHandler { EnableMultipleHttp2Connections=false,UseProxy=false };
    using var http=new HttpClient(handler) { BaseAddress=new Uri($"http://127.0.0.1:{port}"),DefaultRequestVersion=HttpVersion.Version20,DefaultVersionPolicy=HttpVersionPolicy.RequestVersionExact,Timeout=TimeSpan.FromSeconds(10) };
    using var channel=GrpcChannel.ForAddress(http.BaseAddress,new GrpcChannelOptions { HttpClient=http });
    var client=new StudentService.StudentServiceClient(channel);
    var expected=Fixture.Json(limit);
    async Task Call() {
        if(protocol=="rest") {
            using var response=await http.GetAsync($"/api/students?limit={limit}",HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if(response.Version!=HttpVersion.Version20) throw new Exception("REST did not use HTTP/2");
            using var stream=await response.Content.ReadAsStreamAsync();
            var data=await JsonSerializer.DeserializeAsync<Batch>(stream,Fixture.JsonOptions) ?? throw new Exception("Null JSON");
            if(data.Students.Length!=limit) throw new Exception("Wrong count");
            for(int i=0;i<limit;i++) if(data.Students[i]!=expected.Students[i]) throw new Exception("Wrong REST content");
        } else {
            using var call=client.ListStudentsAsync(new ListRequest {Limit=limit},deadline:DateTime.UtcNow.AddSeconds(10));
            var data=await call.ResponseAsync;
            if(data.Students.Count!=limit) throw new Exception("Wrong count");
            for(int i=0;i<limit;i++) {
                var a=data.Students[i];var e=expected.Students[i];
                if(a.StudentCode!=e.StudentCode || a.FullName!=e.FullName || a.Major!=e.Major) throw new Exception("Wrong gRPC content");
            }
        }
    }
    // Validate one call, then warm up using the exact same concurrency as the measured phase.
    await Call();
    async Task<(List<double>[] Samples,int Errors,double Elapsed)> Phase(double duration,bool capture) {
        var sw=Stopwatch.StartNew();int errors=0;
        var samples=Enumerable.Range(0,concurrency).Select(_=>new List<double>()).ToArray();
        await Task.WhenAll(Enumerable.Range(0,concurrency).Select(async worker=> {
            while(sw.Elapsed.TotalSeconds<duration) {
                long start=Stopwatch.GetTimestamp();
                try { await Call(); if(capture) samples[worker].Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
                catch { Interlocked.Increment(ref errors); }
            }
        }));
        return (samples,errors,sw.Elapsed.TotalSeconds);
    }
    var warm=await Phase(warmup,false);
    if(warm.Errors>0) throw new Exception("Warmup failed: "+warm.Errors);
    using var process=Process.GetCurrentProcess();
    using var server=Process.GetProcessById(int.Parse(Opt("server-pid","0")));
    process.Refresh();server.Refresh();var clientCpu=process.TotalProcessorTime;var serverCpu=server.TotalProcessorTime;
    var phase=await Phase(seconds,true);
    process.Refresh();server.Refresh();
    double clientCpuSeconds=(process.TotalProcessorTime-clientCpu).TotalSeconds,serverCpuSeconds=(server.TotalProcessorTime-serverCpu).TotalSeconds;
    var sorted=phase.Samples.SelectMany(s=>s).Order().ToArray();
    if(sorted.Length==0) throw new Exception("No successful samples");
    double Percentile(double q)=>sorted[Math.Clamp((int)Math.Ceiling(q*sorted.Length)-1,0,sorted.Length-1)];
    var result=new {
        protocol,limit,concurrency,requestedSeconds=seconds,warmupSeconds=warmup,elapsedSeconds=phase.Elapsed,
        successfulRequests=sorted.Length,errors=phase.Errors,errorRate=(double)phase.Errors/(sorted.Length+phase.Errors),
        requestsPerSecond=sorted.Length/phase.Elapsed,p50Ms=Percentile(.50),p95Ms=Percentile(.95),p99Ms=Percentile(.99),
        clientCpuSeconds,serverCpuSeconds,clientCpuCores=clientCpuSeconds/phase.Elapsed,serverCpuCores=serverCpuSeconds/phase.Elapsed,
        responsePayloadBytes=protocol=="rest"?JsonSerializer.SerializeToUtf8Bytes(Fixture.Json(limit),Fixture.JsonOptions).Length:Fixture.Proto(limit).CalculateSize(),
        httpVersion="2.0",tls=false,compression=false,responseCache=false,
        runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,logicalProcessors=Environment.ProcessorCount,
        timestampUtc=DateTime.UtcNow.ToString("O")
    };
    await File.WriteAllTextAsync(output,JsonSerializer.Serialize(result,new JsonSerializerOptions {WriteIndented=true}));
    Console.WriteLine($"{protocol} n={limit} c={concurrency}: {result.requestsPerSecond:F0}/s, p95={result.p95Ms:F3}ms, errors={phase.Errors}");
} else throw new ArgumentException("Use server or load");

public record StudentDto(string StudentCode,string FullName,string Major);
public record Batch(StudentDto[] Students);
public static class Fixture {
    public static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    // Identical immutable source records; no serialized response caching.
    private static readonly StudentDto[] Data=Enumerable.Range(1,1000).Select(i=>new StudentDto($"SE{i:000000}",$"Student {i:0000}","Software Engineering")).ToArray();
    public static Batch Json(int limit)=>new(Data.Take(limit).ToArray());
    public static ListReply Proto(int limit) {
        var result=new ListReply();
        foreach(var s in Data.Take(limit)) result.Students.Add(new Student {StudentCode=s.StudentCode,FullName=s.FullName,Major=s.Major});
        return result;
    }
}
public class StudentsGrpc:StudentService.StudentServiceBase {
    public override Task<ListReply> ListStudents(ListRequest request,ServerCallContext context) {
        if(request.Limit is <1 or >1000) throw new RpcException(new Status(StatusCode.InvalidArgument,"limit must be 1..1000"));
        return Task.FromResult(Fixture.Proto(request.Limit));
    }
}
