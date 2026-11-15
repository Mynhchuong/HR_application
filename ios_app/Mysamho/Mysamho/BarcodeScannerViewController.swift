import UIKit
import VisionKit
import Vision
import AVFoundation

/// Màn hình quét mã vạch gốc cho tem máy TPM — dùng VisionKit `DataScannerViewController` (iOS 16+,
/// model nhận diện trên thiết bị của Apple), KHÔNG dùng AVCaptureMetadataOutput thô nữa.
///
/// Lý do đổi (báo lỗi thật 2026-10-24: "cam ios quét hoài không ăn"): AVCaptureMetadataOutput chỉ
/// decode được khi khung hình ĐÃ nét hoàn toàn, không chịu được mờ nhẹ/góc nghiêng/khoảng cách xa —
/// đã thử bỏ autoFocusRangeRestriction=.near (fix lần 1) nhưng vẫn không đọc được kể cả trong môi
/// trường tốt (test bằng mã Code128 hiển thị trên màn hình laptop, cách bình thường). Image cho thấy
/// camera chưa lấy nét kịp — DataScannerViewController dùng model nhận diện (không phải decode khung
/// tĩnh), chịu mờ/góc nghiêng/khoảng cách tốt hơn nhiều, có sẵn hướng dẫn "lại gần hơn" tự động
/// (isGuidanceEnabled), đúng use-case quét tem dán trên máy trong môi trường nhà máy.
/// Đọc cả CODE128 (tem máy hiện tại) lẫn QR (yêu cầu 2026-10-24: hỗ trợ luôn QR cho tương lai đổi
/// loại tem, không cần sửa code lại). Kết quả trả qua closure; ViewController đẩy tiếp vào trang web
/// (window.onNativeBarcode...).
@available(iOS 16.0, *)
final class BarcodeScannerViewController: UIViewController, DataScannerViewControllerDelegate {

    var onResult: ((String) -> Void)?
    var onCancel: (() -> Void)?
    var onError: ((String) -> Void)?

    private var scanner: DataScannerViewController?
    private var finished = false

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
        buildOverlay()

        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            startScanner()
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .video) { [weak self] granted in
                DispatchQueue.main.async {
                    granted ? self?.startScanner() : self?.fail("camera_denied")
                }
            }
        default:
            fail("camera_denied")
        }
    }

    override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        scanner?.stopScanning()
    }

    // MARK: - Scanner

    private func startScanner() {
        guard DataScannerViewController.isSupported, DataScannerViewController.isAvailable else {
            fail("camera_unavailable")
            return
        }

        let dataScanner = DataScannerViewController(
            recognizedDataTypes: [.barcode(symbologies: [.code128, .qr])],
            qualityLevel: .accurate,
            recognizesMultipleItems: false,
            isHighFrameRateTrackingEnabled: true,
            isPinchToZoomEnabled: true,
            isGuidanceEnabled: true,
            isHighlightingEnabled: true
        )
        dataScanner.delegate = self

        addChild(dataScanner)
        dataScanner.view.frame = view.bounds
        dataScanner.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.insertSubview(dataScanner.view, at: 0)
        dataScanner.didMove(toParent: self)
        scanner = dataScanner

        do {
            try dataScanner.startScanning()
        } catch {
            fail("camera_unavailable")
        }
    }

    func dataScanner(_ dataScanner: DataScannerViewController,
                      didAdd addedItems: [RecognizedItem],
                      allItems: [RecognizedItem]) {
        guard !finished else { return }
        for item in addedItems {
            if case .barcode(let barcode) = item,
               let value = barcode.payloadStringValue, !value.isEmpty {
                finished = true
                UINotificationFeedbackGenerator().notificationOccurred(.success)
                onResult?(value)
                return
            }
        }
    }

    func dataScannerDidZoom(_ dataScanner: DataScannerViewController) {}

    func dataScanner(_ dataScanner: DataScannerViewController, becameUnavailableWithError error: DataScannerViewController.ScanningUnavailable) {
        fail("camera_unavailable")
    }

    // MARK: - UI (nút Đóng + gợi ý, đè lên trên camera preview của DataScannerViewController)

    private func buildOverlay() {
        let hint = UILabel()
        hint.translatesAutoresizingMaskIntoConstraints = false
        hint.text = "Đưa mã vạch/QR trên tem máy vào khung hình"
        hint.textColor = .white
        hint.font = .systemFont(ofSize: 15, weight: .semibold)
        hint.textAlignment = .center
        hint.numberOfLines = 0
        hint.layer.shadowColor = UIColor.black.cgColor
        hint.layer.shadowOpacity = 0.8
        hint.layer.shadowRadius = 3
        hint.layer.shadowOffset = .zero
        view.addSubview(hint)

        let close = UIButton(type: .system)
        close.translatesAutoresizingMaskIntoConstraints = false
        close.setTitle("Đóng", for: .normal)
        close.setTitleColor(.white, for: .normal)
        close.titleLabel?.font = .systemFont(ofSize: 17, weight: .semibold)
        close.addTarget(self, action: #selector(cancelTapped), for: .touchUpInside)
        view.addSubview(close)

        NSLayoutConstraint.activate([
            hint.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor, constant: -24),
            hint.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 24),
            hint.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -24),

            close.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor, constant: 12),
            close.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 20),
        ])
    }

    @objc private func cancelTapped() {
        guard !finished else { return }
        finished = true
        onCancel?()
    }

    private func fail(_ message: String) {
        guard !finished else { return }
        finished = true
        onError?(message)
    }
}
