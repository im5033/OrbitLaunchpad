# Orbit Launchpad

仿 macOS Launchpad 的 Windows 應用程式啟動器，使用 C#、WPF 與 .NET 10 開發。

本頁對應 **v0.3.0**。移除搜尋列，改善圖示重疊分組與換位判定，修正關閉右鍵選單時誤收起 Launchpad。

## 下載與啟動

從 [v0.3.0 Release](https://github.com/im5033/OrbitLaunchpad/releases/tag/v0.3.0) 下載 ZIP，解壓縮後執行 `OrbitLaunchpad.exe`。
可在 [所有版本](https://github.com/im5033/OrbitLaunchpad/releases) 查看歷史發佈。

此歷史版本需要 Windows x64 與 .NET Desktop Runtime 10 x64。

首次啟動會讀取桌面及開始功能表的捷徑，建立初始項目。
佈局與設定儲存在程式旁的 `data` 資料夾。升級時先從系統匣結束舊程式，再替換執行檔並保留 `data`。

## 操作

| 操作 | 功能 |
| --- | --- |
| Ctrl + Alt + Space | 預設開啟／收起快捷鍵 |
| 點擊圖示 | 啟動程式、檔案或一般資料夾 |
| 拖曳圖示 | 整理位置、交換排列 |
| 圖示重疊 | 建立分類資料夾或加入現有分類 |
| 點分類資料夾 | 展開內容並編輯名稱 |
| 點空白處 | 逐層返回或收起 Launchpad |
| 右鍵圖示／空白處 | 開啟項目或主選單 |
| 系統匣圖示 | 開啟、收起或結束程式 |

## 從原始碼建置

使用 Windows x64 與 .NET 10 SDK，在儲存庫根目錄執行。

```powershell
.\build.ps1 -Edition Lite
.\build.ps1 -Edition Full
```

輸出位於 `artifacts/Lite` 或 `artifacts/Full`。

執行內建檢查。

```powershell
.\artifacts\Lite\OrbitLaunchpad.exe --self-test
```

結果寫入執行檔旁的 `self-test.txt`。`--review` 使用獨立的 `review-data` 目錄。

## 版本紀錄

各標籤的應用程式原始碼由本次開發時保存的修改紀錄還原，依交付順序提交。
README 與建置腳本已整理成適合此儲存庫的形式，Git 提交與 Release 的建立日期為匯入日期。
每個 Release 的執行檔取自當時保存的打包檔。
公開下載包使用乾淨的初始狀態，個人佈局可在使用者自己的電腦匯入。
