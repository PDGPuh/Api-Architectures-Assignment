# Kết quả đo thực tế: REST/JSON và gRPC/Protobuf

**Loại thí nghiệm:** pilot cục bộ; không phải benchmark production hoặc bằng chứng một kiến trúc luôn nhanh hơn.

**Máy:** AMD Ryzen 7 7735HS, 8 lõi vật lý / 16 luồng; Windows x64. Client và server là hai process trên cùng máy, không giới hạn tài nguyên riêng. Code chạy .NET 10, Release. Hai phía dùng HTTP/2 cleartext loopback; không TLS, database, auth, cache response hoặc nén.

**Runtime ghi nhận:** .NET 10.0.10

**Bắt đầu (UTC):** 2026-09-10T01:37:19.825061+00:00  
**Kết thúc (UTC):** 2026-09-10T01:41:44.025696+00:00

Mỗi cấu hình lặp 3 lần; mỗi lần warm-up 2 giây, đo 5 giây. Bảng lấy trung vị 3 lượt. Client giải mã và kiểm tra mọi trường, mọi bản ghi. Số liệu tính cả chi phí ánh xạ, framework và validation.

| Sinh viên | Đồng thời | REST req/s | gRPC req/s | gRPC/REST | REST p95 ms | gRPC p95 ms | Giảm p95 |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 1 | 8,288 | 6,496 | 0.78× | 0.171 | 0.231 | -35.0% |
| 1 | 32 | 149,530 | 95,573 | 0.64× | 0.312 | 0.486 | -55.5% |
| 100 | 1 | 5,047 | 5,078 | 1.01× | 0.278 | 0.309 | -11.1% |
| 100 | 32 | 44,818 | 57,485 | 1.28× | 1.313 | 0.992 | +24.4% |
| 1000 | 1 | 1,255 | 2,115 | 1.68× | 1.132 | 0.887 | +21.7% |
| 1000 | 32 | 5,119 | 8,884 | 1.74× | 11.316 | 7.185 | +36.5% |

Tỷ số >1: gRPC có thông lượng cao hơn; <1: REST cao hơn. Giảm p95 âm: gRPC có p95 cao hơn. Hai chỉ số không bắt buộc cùng thắng.

**Trường hợp gần như ngang nhau:** 100 sinh viên / 1 request đồng thời chỉ chênh khoảng 0,6% thông lượng, khoảng min–max chồng lấn; chưa đủ cơ sở kết luận lợi thế ổn định.

## Dao động giữa các lượt và CPU

| N | C | Giao thức | RPS min–max | p95 min–max (ms) | CPU server (lõi tương đương) | CPU client | Lỗi |
|---:|---:|---|---:|---:|---:|---:|---:|
| 1 | 1 | rest | 8,001–8,397 | 0.165–0.181 | 1.38 | 1.31 | 0 |
| 1 | 1 | grpc | 6,354–6,664 | 0.218–0.240 | 1.20 | 1.42 | 0 |
| 1 | 32 | rest | 147,014–151,679 | 0.308–0.324 | 4.02 | 5.94 | 0 |
| 1 | 32 | grpc | 94,847–99,372 | 0.471–0.487 | 4.33 | 6.83 | 0 |
| 100 | 1 | rest | 4,923–5,133 | 0.270–0.282 | 1.11 | 1.09 | 0 |
| 100 | 1 | grpc | 4,996–5,085 | 0.308–0.325 | 1.00 | 1.31 | 0 |
| 100 | 32 | rest | 42,804–45,579 | 1.262–1.435 | 4.60 | 8.30 | 0 |
| 100 | 32 | grpc | 57,405–62,081 | 0.960–1.023 | 4.82 | 7.92 | 0 |
| 1000 | 1 | rest | 1,244–1,295 | 1.015–1.248 | 1.09 | 1.19 | 0 |
| 1000 | 1 | grpc | 2,023–2,203 | 0.795–0.887 | 0.62 | 1.02 | 0 |
| 1000 | 32 | rest | 4,846–5,252 | 11.115–13.887 | 3.70 | 9.73 | 0 |
| 1000 | 32 | grpc | 8,253–9,268 | 6.401–8.459 | 4.22 | 7.19 | 0 |

CPU là giây CPU / giây thực, không phải % toàn máy. RPS/p95 min–max giúp thấy độ nhiễu; chưa có kiểm định ý nghĩa thống kê.

## Payload được serialize

| Sinh viên | JSON bytes | Protobuf bytes | Giảm |
|---:|---:|---:|---:|
| 1 | 98 | 48 | 51.0% |
| 100 | 8,414 | 4,800 | 43.0% |
| 1000 | 84,014 | 48,000 | 42.9% |

Chỉ đo body serialized, không đo tổng byte trên mạng. Protobuf chưa tính tiền tố 5 byte của gRPC; cả hai chưa tính HTTP/2/TCP/IP.

## Kết luận theo dữ liệu

- 1 sinh viên, 1 request đồng thời: **REST có thông lượng cao hơn 1.28 lần**. p95 REST=0.171 ms, gRPC=0.231 ms.
- 1 sinh viên, 32 request đồng thời: **REST có thông lượng cao hơn 1.56 lần**. p95 REST=0.312 ms, gRPC=0.486 ms.
- 100 sinh viên, 1 request đồng thời: **gRPC có thông lượng cao hơn 1.01 lần**. p95 REST=0.278 ms, gRPC=0.309 ms.
- 100 sinh viên, 32 request đồng thời: **gRPC có thông lượng cao hơn 1.28 lần**. p95 REST=1.313 ms, gRPC=0.992 ms.
- 1000 sinh viên, 1 request đồng thời: **gRPC có thông lượng cao hơn 1.68 lần**. p95 REST=1.132 ms, gRPC=0.887 ms.
- 1000 sinh viên, 32 request đồng thời: **gRPC có thông lượng cao hơn 1.74 lần**. p95 REST=11.316 ms, gRPC=7.185 ms.

Protobuf nhỏ hơn ở fixture này, có thể góp phần tạo lợi thế khi payload lớn. Tuy nhiên phép đo không cô lập chi phí serialization; không chứng minh đó là nguyên nhân duy nhất. Payload nhỏ có thể bị chi phối bởi overhead framework, scheduling và client/server cùng tranh tài nguyên.

Đây là kết quả của hai implementation cụ thể, trong loopback không TLS. Không suy rộng thành “gRPC luôn nhanh nhất” hoặc đổi quyết định REST của case study chỉ dựa trên pilot. REST còn được chọn vì client web, nghiệp vụ CRUD và chi phí triển khai.

## Tái lập và nâng chất lượng thí nghiệm

Xem [README thí nghiệm](README.md), [code C#](Program.cs), [hợp đồng proto](Protos/students.proto), [script chạy](run.py), [toàn bộ kết quả JSON](results/summary.json), [CSV](results/summary.csv).

Chạy dài hơn, tách client/server, cố định tài nguyên, bật TLS ở cả hai và ghi nhận tải nền trước khi sử dụng kết quả để quyết định production. Ma trận dài có sẵn trong README. Không công bố p95 của pilot là p95 người dùng Internet.
