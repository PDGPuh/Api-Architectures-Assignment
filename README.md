# Comparing API Architectures — SE1917

Bài trình bày 28 slide so sánh SOAP, REST API, GraphQL và gRPC theo 14 tiêu chí; chọn REST cho Student Management System.

## Nhóm thực hiện

- **Phan Thị Thảo Vy** — Nhóm trưởng
- Phạm Đinh Gia Phú
- Tăng Minh Trọng
- Lữ Phước Nhật Tú

**Lớp:** SE1917  
**Giảng viên:** PhuongLHK

## Xem bài

[Website bài Assignment](https://PDGPuh.github.io/Api-Architectures-Assignment/)

Mở `index.html` bằng trình duyệt để xem offline. File độc lập, không cần cài thư viện.

- Chế độ trình chiếu và đọc toàn bài; phím trái/phải chuyển slide.
- Sơ đồ kiến trúc ở slide 15, sơ đồ giao tiếp ở slide 16.
- Demo mô phỏng ở slide 21: 200, 400, 401, 403, 404.
- Hợp đồng OpenAPI tải được, 15 nguồn kỹ thuật chính thức.
- Nút **In / PDF** xuất bộ slide 16:9; kiểm tra preview đủ 28 trang.
- Nút **Thông tin nhóm** cho phép cập nhật và tải HTML mới.

Demo sử dụng dữ liệu giả lập trong JavaScript, không gọi backend thật. GitHub Pages lưu trữ trang tĩnh; phù hợp yêu cầu đề không cần ứng dụng hoàn chỉnh.

## Cập nhật

Sửa `index.html`, commit và push vào `main`. GitHub Pages xuất bản từ nhánh `main`, thư mục root. Đợi deployment thành công rồi kiểm tra website.

Đề gốc: Comparing API Architectures.pdf. Bố cục tham khảo: So-sanh-4-kien-truc-API.pdf; nội dung kỹ thuật được kiểm chứng và viết lại.

## Thí nghiệm gRPC và REST đã chạy thực tế

Xem slide **24–26** trên website. Đã chạy 36 lượt đo trên .NET 10; kết quả gồm cả trường hợp REST thắng và gRPC thắng.

- [Thiết kế và cách chạy lại](benchmark/README.md)
- [Báo cáo kết quả thực tế](benchmark/REPORT.md)
- [Mã nguồn backend + client C#](benchmark/Program.cs)
- [Hợp đồng gRPC](benchmark/Protos/students.proto)
- [Số liệu JSON](benchmark/results/summary.json) / [CSV](benchmark/results/summary.csv)

Đây là pilot HTTP/2 loopback không TLS, client/server cùng máy. Backend đã chạy cục bộ; GitHub Pages chỉ đăng slide, mã nguồn và kết quả, không chạy .NET server.
