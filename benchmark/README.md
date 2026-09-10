# Thí nghiệm thực tế: REST/JSON và gRPC/Protobuf

## Mục tiêu và giả thuyết

Kiểm chứng liệu gRPC có giảm độ trễ hoặc tăng thông lượng so với REST/JSON khi đọc danh sách sinh viên. Không đặt điều kiện rằng gRPC phải thắng. Kết quả chỉ áp dụng cho implementation và môi trường được mô tả.

Hai API trả cùng dữ liệu và cùng trường: `studentCode`, `fullName`, `major`.

- REST: `GET /api/students?limit=100`, JSON `{ "students": [...] }`.
- gRPC: `StudentService.ListStudents({ limit: 100 })`, message `ListReply`.
- Mô tả RPC và message: `Protos/students.proto`.
- Code backend và client đo tải: `Program.cs`.

## Điều kiện được giữ giống nhau

Cùng ứng dụng ASP.NET Core/Kestrel, .NET 10, Release, cùng dữ liệu nguồn bất biến trong RAM. Cả hai dùng HTTP/2 cleartext trên loopback `127.0.0.1`. Không TLS, xác thực, database, nén hoặc cache response. Tái sử dụng HTTP client/kết nối; không tạo kết nối mới cho mỗi yêu cầu. Mỗi lượt chỉ đo một giao thức; server dùng chung suốt phiên đo.

Dữ liệu là chuỗi ASCII giả lập để tránh việc JSON escape tiếng Việt trở thành một biến gây nhiễu. REST dùng System.Text.Json và DTO; gRPC dùng mã sinh từ Protobuf. Mỗi request dựng biểu diễn phản hồi mới, không cache sẵn byte đã serialize. Phần ánh xạ DTO/message cũng được tính vào phép đo: đây là so sánh hai API cụ thể, không cô lập riêng serializer.

## Thiết kế lượt đo thử đã chạy

- Kích thước: 1, 100, 1.000 sinh viên mỗi response.
- Đồng thời: 1 và 32 worker, mỗi worker có tối đa một request đang chờ.
- Lặp lại: 3 lần cho mỗi cấu hình/giao thức, tổng cộng 36 lượt.
- Mỗi lượt: 2 giây warm-up, 5 giây đo, sau đó đợi request đang chạy hoàn tất.
- Thứ tự trong mỗi cấu hình: REST→gRPC, gRPC→REST, REST→gRPC.
- Không retry tự động trong harness; lỗi request và timeout được đếm, không giấu khỏi báo cáo.
- Client kiểm tra HTTP/2 ở REST, decode toàn bộ payload và so sánh mọi trường của mọi bản ghi với dữ liệu kỳ vọng.
- Đo thời gian từ trước khi gửi đến sau khi decode/kiểm tra; kết nối và warm-up không tính vào latency đo chính.

## Chỉ số và cách tổng hợp

- Throughput: số request **thành công** / thời gian thực của lượt đo, bao gồm thời gian drain.
- p50/p95/p99: percentile nearest-rank của thời gian các request thành công trong mỗi lượt. Lỗi được báo riêng.
- Bảng tổng hợp lấy trung vị của 3 giá trị mỗi chỉ số. Trung vị của p95 từng lượt **không phải** p95 gộp mọi request.
- Có min/max của RPS và p95 giữa 3 lần để thấy độ dao động. Không tuyên bố ý nghĩa thống kê từ 3 lần đo ngắn.
- CPU: tổng giây CPU process / thời gian đo, đơn vị số lõi CPU tương đương; không phải % toàn máy.
- Payload bytes: số byte JSON UTF-8 hoặc Protobuf serialized. **Không phải tổng byte trên mạng**, không tính HTTP/2 header/frame, TCP/IP hoặc tiền tố message 5 byte của gRPC.
- Closed-loop: khi server chậm, worker gửi ít request hơn. Latency không phản ánh thời gian chờ của một luồng đến cố định ngoài harness.

## Chạy lại trên Windows

Cần .NET 10 SDK và Python 3. Package NuGet được khóa trong `packages.lock.json`.

```powershell
cd benchmark
python run.py --output results-my-run
```

Nếu `python` không có trên PATH, dùng `py -3 run.py --output results-my-run` hoặc đường dẫn Python thực tế. Script build Release, bật server ẩn, chạy đo và dừng đúng process server nó đã tạo. Không đóng ứng dụng khác; nếu port đang dùng, chọn `--port 5088`.

Chạy lâu hơn, theo ma trận ban đầu (khoảng 2 giờ 15 phút, chưa kể build/drain):

```powershell
python run.py --seconds 60 --warmup 30 --repeats 5 --sizes 1,100,1000 --concurrency 1,10,100 --output results-long
```

Thời lượng = 3 kích thước × 3 mức đồng thời × 5 lần × 2 giao thức × (30 + 60) giây.

Thư mục kết quả khác giúp giữ nguyên số liệu đã nộp. Không đổi tên hoặc chỉnh tay số liệu rồi gọi là kết quả chạy lại.

## Đọc kết quả và tệp minh chứng

- `results/summary.json`: cấu hình, thứ tự chạy, thời điểm, giới hạn, các lượt đo và bảng tổng hợp.
- `results/summary.csv`: bảng tổng hợp mở bằng Excel.
- `results/rest-n100-c32-r1.json` và các file cùng mẫu: từng lượt đo; không chứa mẫu latency của từng request.
- `REPORT.md`: nhận xét dựa trên kết quả thực tế.

Công thức:

```text
Tỷ số thông lượng = RPS_gRPC / RPS_REST
Giảm p95 (%) = (p95_REST - p95_gRPC) / p95_REST × 100
Giảm payload (%) = (bytes_JSON - bytes_Protobuf) / bytes_JSON × 100
```

Tỷ số RPS > 1 nghĩa là gRPC có thông lượng cao hơn trong cấu hình đó. Giảm p95 âm nghĩa là gRPC có p95 cao hơn (chậm hơn theo chỉ số này).

## Giới hạn và cách mở rộng

Đây là pilot trên một máy: client/server tranh CPU, RAM và tài nguyên hệ điều hành; không có resource isolation, mạng thật hoặc TLS. Lượt đo ngắn chịu ảnh hưởng JIT, GC, scheduler, nhiệt độ, tiến trình nền và thứ tự chạy. Không áp dụng trực tiếp số liệu này cho production, browser/gRPC-Web hoặc hệ thống có database.

Để củng cố kết luận: chạy dài hơn; lặp nhiều lần; đảo/ngẫu nhiên hóa thứ tự cân bằng; tách client sang máy khác; giám sát để client không là nút thắt; cố định tài nguyên; bật TLS ở cả hai; đo thêm nén, mạng có độ trễ và database thực. Thử cache GET là một kịch bản riêng, không trộn với so sánh không-cache.

Không suy ra Protobuf là nguyên nhân duy nhất chỉ từ kết quả end-to-end. Để kiểm tra nguyên nhân, bổ sung microbenchmark serialization cùng dữ liệu hoặc một endpoint HTTP/2 trả Protobuf làm đối chứng.

## Nguồn kỹ thuật

- [Microsoft: gRPC with ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore?view=aspnetcore-10.0)
- [Microsoft: gRPC performance best practices](https://learn.microsoft.com/en-us/aspnet/core/grpc/performance?view=aspnetcore-10.0)
- [Microsoft: HTTP/2 without TLS](https://learn.microsoft.com/en-us/aspnet/core/grpc/troubleshoot?view=aspnetcore-10.0)
- [gRPC: Benchmarking](https://grpc.io/docs/guides/benchmarking/)
- [Protocol Buffers overview](https://protobuf.dev/overview/)

GitHub Pages chỉ phục vụ HTML và kết quả tĩnh. Backend benchmark chạy trên máy có .NET; nút demo trong deck không tạo số liệu benchmark.
