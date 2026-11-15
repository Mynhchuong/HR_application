//
//  SceneDelegate.swift
//  Mysamho
//
//  Created by Nguyễn Minh Trường on 6/5/26.
//

import UIKit
import WidgetKit

class SceneDelegate: UIResponder, UIWindowSceneDelegate {

    var window: UIWindow?


    func scene(_ scene: UIScene, willConnectTo session: UISceneSession, options connectionOptions: UIScene.ConnectionOptions) {
        // App đang tắt hẳn mà bấm nút tắt: iOS giao item qua đây chứ KHÔNG gọi performActionFor.
        // Giữ lại đường dẫn để loadWebApp mở thẳng, khỏi ghé trang chủ.
        if let item = connectionOptions.shortcutItem,
           let path = item.userInfo?[AppShortcuts.pathKey] as? String {
            ViewController.pendingOpenPath = path
        }
        // Use this method to optionally configure and attach the UIWindow `window` to the provided UIWindowScene `scene`.
        // If using a storyboard, the `window` property will automatically be initialized and attached to the scene.
        // This delegate does not imply the connecting scene or session are new (see `application:configurationForConnectingSceneSession` instead).
        guard let _ = (scene as? UIWindowScene) else { return }
    }

    func sceneDidDisconnect(_ scene: UIScene) {
        // Called as the scene is being released by the system.
        // This occurs shortly after the scene enters the background, or when its session is discarded.
        // Release any resources associated with this scene that can be re-created the next time the scene connects.
        // The scene may re-connect later, as its session was not necessarily discarded (see `application:didDiscardSceneSessions` instead).
    }

    // Người dùng bấm nút tắt khi app đang chạy nền
    func windowScene(_ windowScene: UIWindowScene,
                     performActionFor shortcutItem: UIApplicationShortcutItem,
                     completionHandler: @escaping (Bool) -> Void) {
        completionHandler(AppShortcuts.handle(shortcutItem))
    }

    func sceneDidBecomeActive(_ scene: UIScene) {
        // Quay lại app: số chưa đọc có thể đã đổi ở nơi khác (đọc trên web, sếp vừa duyệt đơn...).
        // Chỉnh lại badge + vẽ lại widget để icon không đứng số cũ.
        WidgetBridge.refreshBadge()
        WidgetCenter.shared.reloadAllTimelines()
    }

    func sceneWillResignActive(_ scene: UIScene) {
        // Called when the scene will move from an active state to an inactive state.
        // This may occur due to temporary interruptions (ex. an incoming phone call).
    }

    func sceneWillEnterForeground(_ scene: UIScene) {
        // Called as the scene transitions from the background to the foreground.
        // Use this method to undo the changes made on entering the background.
    }

    func sceneDidEnterBackground(_ scene: UIScene) {
        // Called as the scene transitions from the foreground to the background.
        // Use this method to save data, release shared resources, and store enough scene-specific state information
        // to restore the scene back to its current state.
    }


}

