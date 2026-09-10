"""Reproducible local pilot. Requires Python 3 and .NET 10 SDK; standard library only."""
import argparse,json,subprocess,time,socket,platform,statistics,datetime,os,csv,hashlib
from pathlib import Path
p=argparse.ArgumentParser()
p.add_argument('--seconds',type=float,default=5)
p.add_argument('--warmup',type=float,default=2)
p.add_argument('--repeats',type=int,default=3)
p.add_argument('--sizes',default='1,100,1000')
p.add_argument('--concurrency',default='1,32')
p.add_argument('--port',type=int,default=5087)
p.add_argument('--output',default='results')
a=p.parse_args()
if a.seconds<=0 or a.warmup<0 or a.repeats<1: p.error('Invalid duration or repeat count')
root=Path(__file__).resolve().parent;out=(root/a.output).resolve();out.mkdir(parents=True,exist_ok=True)
flags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0
subprocess.run(['dotnet','build',str(root/'ApiBenchmark.csproj'),'-c','Release'],check=True,creationflags=flags)
dll=root/'bin/Release/net10.0/ApiBenchmark.dll'
# Refuse an occupied port: never terminate another application's server.
with socket.socket() as probe:
 if probe.connect_ex(('127.0.0.1',a.port))==0: raise SystemExit('Port is occupied; choose --port')
log=(out/'server.log').open('w',encoding='utf-8')
server=subprocess.Popen(['dotnet',str(dll),'server','--port',str(a.port)],stdout=log,stderr=subprocess.STDOUT,creationflags=flags)
rows=[];order=[]
try:
 for _ in range(100):
  if server.poll() is not None: raise RuntimeError('Server exited; see server.log')
  with socket.socket() as probe:
   if probe.connect_ex(('127.0.0.1',a.port))==0: break
  time.sleep(.1)
 else: raise RuntimeError('Server did not start')
 started=datetime.datetime.now(datetime.timezone.utc).isoformat()
 for size in map(int,a.sizes.split(',')):
  for concurrency in map(int,a.concurrency.split(',')):
   for repeat in range(1,a.repeats+1):
    protocols=['rest','grpc'] if repeat%2 else ['grpc','rest']
    for protocol in protocols:
     path=out/f'{protocol}-n{size}-c{concurrency}-r{repeat}.json'
     cmd=['dotnet',str(dll),'load','--port',str(a.port),'--protocol',protocol,'--size',str(size),'--concurrency',str(concurrency),'--seconds',str(a.seconds),'--warmup',str(a.warmup),'--server-pid',str(server.pid),'--output',str(path)]
     subprocess.run(cmd,check=True,creationflags=flags,timeout=a.seconds+a.warmup+60)
     row=json.loads(path.read_text(encoding='utf-8'));row['repeat']=repeat;rows.append(row);order.append(path.name)
 summary=[]
 for size in map(int,a.sizes.split(',')):
  for concurrency in map(int,a.concurrency.split(',')):
   for protocol in ['rest','grpc']:
    group=[r for r in rows if r['limit']==size and r['concurrency']==concurrency and r['protocol']==protocol]
    summary.append({'protocol':protocol,'limit':size,'concurrency':concurrency,'repeats':len(group),
      **{key:statistics.median(r[key] for r in group) for key in ['requestsPerSecond','p50Ms','p95Ms','p99Ms','serverCpuCores','clientCpuCores','responsePayloadBytes']},
      'rpsMin':min(r['requestsPerSecond'] for r in group),'rpsMax':max(r['requestsPerSecond'] for r in group),
      'p95Min':min(r['p95Ms'] for r in group),'p95Max':max(r['p95Ms'] for r in group),
      'errors':sum(r['errors'] for r in group),'successfulRequests':sum(r['successfulRequests'] for r in group)})
 metadata={'startedUtc':started,'finishedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'os':platform.platform(),'architecture':platform.machine(),'logicalProcessors':os.cpu_count(),'configuration':vars(a),'order':order,'transport':'HTTP/2 cleartext loopback, both protocols','limitations':['Client and server share one machine; no resource isolation.','No TLS, database, auth, compression or HTTP response cache.','Closed-loop load; latency excludes waiting outside each worker.','Short local pilot; median of per-run percentiles is not a pooled percentile.','Payload bytes exclude HTTP/2 headers/framing and the 5-byte gRPC message prefix.','DTO mapping, framework behavior and client validation are included; not a serialization-only benchmark.']}
 metadata['sourceSha256']={name:hashlib.sha256((root/name).read_bytes()).hexdigest() for name in ['Program.cs','Protos/students.proto','ApiBenchmark.csproj','packages.lock.json']}
 (out/'summary.json').write_text(json.dumps({'metadata':metadata,'summary':summary,'runs':rows},indent=2),encoding='utf-8')
 with (out/'summary.csv').open('w',newline='',encoding='utf-8') as f:
  writer=csv.DictWriter(f,fieldnames=summary[0].keys());writer.writeheader();writer.writerows(summary)
 print('DONE:',str(out/'summary.json'),flush=True)
finally:
 server.terminate()
 try: server.wait(timeout=10)
 except subprocess.TimeoutExpired: server.kill();server.wait()
 log.close()
