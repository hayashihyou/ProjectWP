/// <summary>
/// シーン名をまとめて置く場所。
///
/// シーン名を各スクリプトに直接 "02_PlayerJoin" のように書くと、シーンの名前を変えたときに
/// 直し漏れが出ても気づけない(コンパイルは通ってしまい、実行して初めて遷移に失敗する)。
/// ここに1か所だけ書いておき、他のスクリプトからは SceneNames.PlayerJoin のように参照する。
///
/// シーンを追加・改名したら、ここと Build Settings(File > Build Profiles の Scene List)の
/// 両方を直すこと。Build Settings に無いシーンは読み込めない。
/// </summary>
public static class SceneNames
{
    // ---------- ゲーム本編の流れ ----------

    /// <summary>タイトル画面 + サーバー選択。起動シーン(NetworkManagerもここにある)</summary>
    public const string Title = "01_Title";

    /// <summary>待機画面(参加人数の確認)。1P〜4Pの枠にキャラクターが並ぶ</summary>
    public const string PlayerJoin = "02_PlayerJoin";

    /// <summary>ミニゲーム一覧(今はまだ中身の無いプレースホルダー)</summary>
    public const string StageSelect = "03_StageSelect";

    /// <summary>連続ストップウォッチ</summary>
    public const string Stopwatch = "MG_Stopwatch";

    /// <summary>パイ投げ</summary>
    public const string PieThrow = "MG_PieThrow";

    /// <summary>カメラの中心に映れ</summary>
    public const string CameraCenter = "MG_CameraCenter";


    ///<summary>ミニゲームの一覧(一覧画面の並び順や「もう一度」の判定に使う)</summary>
    public static readonly string[] MiniGames = { PieThrow, Stopwatch, CameraCenter };

    // ---------- 動作確認用 ----------

    /// <summary>キャラクターの移動確認用</summary>
    public const string TestPlayer = "Test_Player";

    /// <summary>フェード付き遷移の確認用(SceneLoader + FadeCanvas が置いてある)</summary>
    public const string Sample = "SampleScene";

    /// <summary>フェード付き遷移の確認用(SampleSceneからの遷移先)</summary>
    public const string Test = "TestScene";
}
