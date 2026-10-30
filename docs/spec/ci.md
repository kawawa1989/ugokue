# CI のテスト

GitHub Actions（`.github/workflows/ci.yml`）で自動実行するテストの一覧。テストを追加・変更・削除する PR では、同じ PR でこのページを更新する。

## 実行のタイミング

- PR を作成・更新したとき
- main に push されたとき
- Actions 画面から手動で実行したとき（`workflow_dispatch`）

同じブランチで新しい実行が始まると、古い実行は中止される。

## ジョブの流れ

```mermaid
flowchart LR
    dotnet[.NET tests]
    format[Format tests]
    license[Unity license check] --> unitytest[Unity EditMode tests]
    license --> build[Unity build<br/>Windows / Mac]
    build --> smoke[Launch and record<br/>Windows / Mac]
```

| ジョブ | 実行環境 | 内容 |
| --- | --- | --- |
| .NET tests | ubuntu | Unity に依存しないコードの単体テスト（`dotnet test dotnet/Ugokue.sln`） |
| Format tests | ubuntu | 中間形式のサンプルと固定テストモデルの検証、Python のテスト、Python → C# → Python の往復 |
| Unity license check | ubuntu | Secrets `UNITY_LICENSE`、`UNITY_EMAIL`、`UNITY_PASSWORD` が登録されているかを確かめる。欠けていれば失敗し、Unity のジョブは実行されない |
| Unity EditMode tests | ubuntu（game-ci の Docker イメージ） | Unity エディター上のテスト。結果は PR のチェックとして表示される |
| Unity build | ubuntu（game-ci の Docker イメージ） | Windows（StandaloneWindows64）と Mac（StandaloneOSX）のプレイヤーをビルドし、成果物として保存する |
| Launch and record | windows-latest、macos-latest | ビルドしたプレイヤーを各 OS で起動し、計測の記録を検証する |

Unity エディターは game-ci が実行ごとに Docker イメージとして取得する。バージョンは `unity/ProjectSettings/ProjectVersion.txt` に従う。

## 必須チェック

main の保護ルールで、上の表の全ジョブ（Windows と Mac の両方）を必須チェックにしている。ジョブを追加した PR では、マージ後に人が保護ルールへ追加する。どれかが通らない PR はマージできない。必須チェックはジョブの表示名で照合するため、ジョブ名や matrix の値を変える PR では、人が保護ルールの必須チェックも同時に更新する。game-ci が作る「Test Results」チェックは必須にしていない。

## テストの一覧

### .NET tests

| クラス | テスト | 確かめること |
| --- | --- | --- |
| `FrameRecorderTests` | `RecordsEveryFrameForTheRequestedDuration` | 60fps で 10 秒記録すると、フレーム番号 0〜600 が欠けなく並び、最後の時刻が 10 秒以上になる |
| | `IgnoresFramesAfterCompletion` | 記録完了後のフレームは追加されない |
| | `SkipsFramesDuringWarmup` | ウォームアップ中のフレームは記録されず、ウォームアップ後の最初のフレームが時刻 0 になる |
| | `RejectsNonPositiveDuration` | 記録秒数が 0 以下・NaN なら例外になる |
| | `RejectsNegativeWarmup` | ウォームアップ秒数が負・NaN なら例外になる |
| `FrameSummaryTests` | `ComputesAveragesPercentilesAndMaximum` | 平均 fps、平均フレーム時間、p95、p99、最大、平均 CPU 時間が仕様どおりに計算される |
| | `LeavesCpuTimeEmptyWhenNoSampleHasIt` | CPU 時間が1つも取れていなければ平均 CPU 時間は空になる |
| | `RejectsEmptySamples` | フレームが0件なら例外になる |
| `FrameCsvWriterTests` | `WritesOneRowPerSampleWithInvariantNumbers` | 1フレーム1行で書き出し、ドイツ語環境でも小数点がピリオドになる |
| | `WritesSummaryMetrics` | 集計 CSV の項目と値が仕様どおりになる |
| `RollingFrameStatsTests` | `ReturnsZeroBeforeAnyFrame` | フレームがないとき表示値は 0 になる |
| | `AveragesOnlyTheMostRecentWindow` | 直近の指定フレーム数だけで平均する |
| | `RejectsNonPositiveWindow` | 平均に使うフレーム数が 0 以下なら例外になる |
| `RecordingOptionsTests` | `DisablesAutoRecordWithoutArguments` | 計測の引数がなければ自動記録しない。ウォームアップは既定値になる |
| | `ReadsAllRecordingArguments` | 記録秒数、ウォームアップ、出力先、記録後の終了を読み取る |
| | `RejectsInvalidSeconds` | 記録秒数が 0、負、数値以外なら例外になる |
| | `RejectsInvalidWarmup` | ウォームアップ秒数が負、NaN なら例外になる |
| | `RejectsMissingValue` | 値が必要な引数に値がなければ例外になる |

`Format` 名前空間（`dotnet/Ugokue.Core.Tests/Format/ModelPackageTests.cs`）：

| テスト | 確かめること |
| --- | --- |
| `SaveLoadSaveGivesIdenticalBytes` | 書き出し → 読み込み → 書き出しで、model.json とテクスチャのバイト列が一致する |
| `LoadedModelKeepsValues` | 小数を含む値が読み書きで変わらない |
| `WritesPropertiesInSpecOrder` | 項目が仕様の表の順に書かれる |
| `WritesNullParentAndNullPartMesh` | `parent` とパーツの `mesh` が null のとき、null として書かれる |
| `OmitsOptionalFieldsWhenNotSet` | 省略できる項目は、値がなければ書かれない |
| `RejectsUnsupportedVersion`、`AcceptsOtherPatchVersion` | 0.1.x 以外のバージョンを拒否し、PATCH 違いは読む |
| `RejectsWrongFormat`、`RejectsMissingFormat` | `format` が違う・ないファイルを拒否する |
| `RejectsUnknownTopLevelProperty`、`RejectsUnknownNestedProperty`、`RejectsMissingNullableRequiredProperty` | 未定義の項目と、必須項目の欠けを拒否する |
| `KeepsDateLikeStringsAsWritten` | 日付のような文字列を日付に変換しない |
| `LoadRejectsTexturePathOutsideFolder`、`SaveRejectsTexturePathOutsideFolder` | フォルダの外を指すテクスチャのパスを拒否する |
| `LoadRejectsMissingTexture`、`SaveRejectsMissingOrUnreferencedTexture` | テクスチャの欠けと、参照されないテクスチャの書き出しを拒否する |
| `NumbersAreCultureInvariant` | ドイツ語環境でも同じ内容を書き出す |

`Rig` 名前空間（`dotnet/Ugokue.Core.Tests/Rig/`、[実行時の変形と描画](runtime-deformation.md)）：

| クラス | テスト | 確かめること |
| --- | --- | --- |
| `KeyformInterpolatorTests` | `LocatesSegmentAndFraction` | 格子の端、内側の格子点、区間の途中の値で、区間と区間内の位置が正しい |
| | `ClampsValuesOutsideTheGrid` | 格子の範囲外の値は端に収める |
| | `InterpolatesOneAxisWithClamping` | 1軸の補間が端・中間・範囲外で正しい |
| | `InterpolatesBilinearlyBetweenFourCorners` | 2軸の補間が4隅の重み付きの和になる |
| | `ReturnsKeyformExactlyAtGridPoints` | 格子点ではキーフォームの移動量そのものになる |
| | `AddsToExistingOffsets` | 移動量を足し込む（加算合成） |
| | `RejectsMissingGridPoint` | 格子点が欠けたグループを拒否する |
| | `ParameterValuesClampAndIgnoreUnknownIds` | パラメータの値を範囲に収め、未知の ID と NaN を無視する |
| `ModelRigTests` | `DefaultParametersGiveBaseVertices` | 初期値ではすべてのメッシュが基準の形になる |
| | `MatchesAuthoredShapeAtKeyformPoints` | 格子点のパラメータでは、基準の形にそのキーフォームを足した形と一致する |
| | `FaceMatchesVertexCorrespondenceInEveryView` | 各ビューの角度で、顔の頂点が固定テストモデルの頂点の対応と一致する |
| | `ClampsParametersOutsideTheirRange` | 範囲外の値は端の値と同じ形になる |
| | `SelectsNearestViewAndFirstOnTie` | 最も近いビューを選び、同じ距離なら先のものを選ぶ |
| | `DrawListHidesOverriddenPartsAndSortsByDrawOrderThenPartOrder` | ビューで隠したパーツを除き、ビューで上書きした描画順、同じならパーツの並び順に並べる |
| | `DrawListUsesViewDrawOrderOverride` | ビューの描画順の上書き（右上を向いたときの奥の耳）が描画リストに反映される |
| | `RotatesHeadDescendantsAroundAnchorThenTranslatesForBreath` | AngleZ で頭の子孫が anchor のまわりに回り、そのあと Breath で全体が動く。体は回らない |
| `RenderRegressionTests` | `RenderMatchesBaseline`（24通り：個別のパラメータ、10° おきの角度スイープ、デバッグ表示） | 参照レンダラーで描いた固定テストモデルが、基準画像と許容範囲内で一致する |
| | `ComparisonDetectsMovedPart` | 片目を閉じただけの違いを、比較が検出できる |
| `OcclusionTests` | `ClippedPartsStayInsideTheirMasksDuringSweep`（4通りのスイープ） | 1° おきの角度スイープで、黒目が白目から、歯が口の中からはみ出さない |
| | `IrisWouldStickOutWithoutClipping` | 切り抜きをしなければ黒目が白目からはみ出す（上のテストが意味を持つ） |
| | `FrontHairStaysInFrontOfFaceDuringSweep` | ±30° のスイープで、前髪・目・眉が常に顔の手前にある |
| | `FarEarIsHiddenWhenTurnedSideways`（±30°） | 真横を向くと奥の耳が隠れ、手前の耳は見える |
| | `FarEarMovesBehindBackHairWhenTurnedDiagonally`（右上、左下） | 斜めを向くと奥の耳が後ろ髪の後ろに回る |
| | `ViewSwitchIsSeamlessAtBoundary`（28点） | ビューの境界のすぐ手前とすぐ先で、見た目が一致する |
| | `SeamCheckDetectsVisibleDrawOrderSwap` | 境界で見た目が変わる切り替え（横髪を顔の後ろに回す）を検出する |
| | `SweepHasNoFlickerOrLargeJumps`（4通りのスイープ） | 1° ごとの変化が大きすぎず、1° だけ色が変わるちらつきがない |
| | `FlickerCheckDetectsPartMissingForOneDegree` | 1° の間だけ前髪が消えると、ちらつきとして検出する |

`Imaging` 名前空間（`dotnet/Ugokue.Core.Tests/Imaging/PngTests.cs`）：

| テスト | 確かめること |
| --- | --- |
| `EncodeThenDecodeKeepsPixels` | 書き出して読み込むと画素が一致する |
| `DecodesEveryFilterType` | PNG の5種類の行フィルタを正しく戻す |
| `ExpandsRgbAndGrayToRgba` | RGB、グレー、グレー＋アルファを RGBA にする |
| `DecodesEveryTestModelTexture` | 固定テストモデルのテクスチャをすべて読める |
| `RejectsNonPng` | PNG でないデータを拒否する |

`Live` 名前空間（`dotnet/Ugokue.Core.Tests/Live/`、[nizima LIVE からの受信](nizima-live.md)）。`NizimaLiveClientTests` と `ProbeRecorderTests` は、nizima LIVE の応答をまねるテスト用の WebSocket サーバー（`FakeNizimaLiveServer`）を相手に動かす：

| クラス | テスト | 確かめること |
| --- | --- | --- |
| `NizimaProtocolTests` | `BuildsRequestWithApiVersionIdAndData` | リクエストに API のバージョン、種類、メソッド、ID、データ、時刻が入る |
| | `ParsesErrorType` | Error のメッセージから `ErrorType` を読む |
| | `ReturnsNullForUnreadableMessages`（3通り） | JSON でないもの、`Method` のないもの、オブジェクトでないものを読み飛ばす |
| | `ReadsValuesOfFirstOrSelectedModel` | 指定がなければ最初のモデル、指定があればそのモデルの値を読み、ないモデルは null になる |
| | `ReturnsNullWhenNoModelIsLoaded` | `Models` が空・ないときは null になる |
| | `SkipsValuesThatAreNotNumbers` | 数値でない値と `Id` のない値を捨てる |
| | `ReadsLiveParameterDefinitions` | `GetLiveParameters` の名前、作成元、グループ、範囲、初期値、繰り返しを読み、`Id` のないものを捨てる |
| `ReceiveRateMeterTests` | `ReturnsNullWithFewerThanTwoFrames` | 受信が2回未満なら計算しない |
| | `ComputesRateAndIntervals` | 毎秒の受信回数と、受信間隔の平均・p95・最大を計算する |
| | `DropsFramesOlderThanTheWindow` | 計測の幅より古い受信を除く |
| | `RejectsNonPositiveWindow` | 計測の幅が 0 以下なら例外になる |
| `NizimaLiveClientTests` | `FirstConnectionRegistersSavesTokenAndStreamsAfterEnable` | 初回は RegisterPlugin でトークンを保存し、有効化の通知のあとで値の通知とパラメータ一覧を要求し、値を受け取る |
| | `LaterConnectionUsesSavedToken` | 2回目からは保存したトークンで EstablishConnection する |
| | `WaitsForEnableWhenPluginIsDisabled` | プラグインが無効なら、有効化の通知が来るまで値の通知を要求しない |
| | `InvalidTokenRegistersAgain` | トークンが無効なら、消して RegisterPlugin からやり直す |
| | `ReconnectsAfterNizimaLiveRestarts` | nizima LIVE を終了・再起動すると、自動でつなぎ直して値を受け取る |
| | `KeepsRetryingUntilNizimaLiveStarts` | nizima LIVE が起動していなくても落ちず、起動したらつながる |
| | `FrameWithoutModelsKeepsPreviousValues` | モデルのない通知では値を更新せず、モデルがないことを記録する |
| | `IgnoresMalformedMessages` | 読めないメッセージを無視して接続を続ける |
| | `StopEndsTheConnection` | 停止すると `Disconnected` になる |
| | `DefaultsToIpv4LoopbackBecauseNizimaLiveListensOnlyThere` | 既定の接続先が `ws://127.0.0.1:22022/` で、ポートを変えた接続先も 127.0.0.1 になる |
| `RecordedNizimaMessageTests` | `ReadsRegisterPluginResponse`、`ReadsEnableEventAndEstablishResponse` | 実機で記録した登録の応答、有効化の通知、接続の応答を読む（`Live/Fixtures/`、トークンは伏せてある） |
| | `ReadsAllLiveParameterDefinitions` | 実機のパラメータ一覧から 165 個の定義を読み、範囲・初期値・繰り返しが正しい |
| | `ReadsFrameWithTrackedFace`、`ReadsFrameWithoutTrackedFaceAsBaseValues` | 顔を捉えている通知と見失っている通知から、165 個の値を読む。Cubism パラメータは含めない |
| | `ValuesCanExceedTheDeclaredRange` | 実機の値は宣言された範囲を超えることがあり、読み取りでは範囲に収めない |
| | `FramesNameTheLoadedModel` | 通知に読み込んだモデルの ID が入り、ID を指定して読める |
| `TrackingAdapterTests` | `MapsSourceValuesToModelParameters`、`RemapsInputRangeToOutputRange` | nizima LIVE の値を、変換表に従ってモデルのパラメータに置き換える |
| | `ClampsToTheParameterRange`（4通り） | 範囲を超えた値（`Yaw` −39.8、`Roll` 49、目の見開き、負の口の開き）を、パラメータの範囲に収める |
| | `CalibrationMakesTheCurrentPoseFront`、`CalibrationNeedsAValidFrame` | 校正で今の向きが正面になり、目は校正されない。取り消せる。有効なフレームがなければ校正できない |
| | `SmoothingFollowsAtTheSameSpeedAtAnyFrameRate` | 30・60・144 fps で、同じ時間に同じだけ追従する |
| | `DefaultSmoothingKeepsHalfOfThePreviousValuePerSixtiethSecond`、`ModelSmoothingOverridesTheDefault` | 既定の平滑化の強さが 0.5 で、モデルの `tracking.parameters` の値が優先される |
| | `HoldsTheLastValuesWhileTheFaceIsLost`、`HoldsWhenFramesStopArriving` | 顔を見失ったときも、通知が止まったときも、直前の値を保持する |
| | `ReturnsSlowlyToDefaultsAfterTheHold` | 保持のあと、跳ねずに初期値へ戻る |
| | `RecoversWithoutAJump`、`WithoutRecoveryHandlingTheSameInputWouldJump` | 復帰のとき 1/60 秒あたりの変化が 3° 未満に収まる。復帰の処理がなければ跳ねる（上のテストが意味を持つ） |
| | `DoesNotWriteParametersBeforeTheFirstValidFrame`、`WritesOnlyMappedParameters`、`IgnoresMappingsToParametersTheModelDoesNotHave`、`MissingSourceValuesKeepTheParameterDefault` | 入力が来るまで書き込まない。変換表にないパラメータは触らない |
| `TrackingPlaybackTests` | `LoadsRecordedFramesInOrder` | 記録を時刻順に読み、指定した時点の最新のフレームを返す |
| | `ShortFaceLossHoldsThePoseAndRecoversSmoothly`、`RawInputJumpsWhereTheAdapterDoesNot` | 実機の記録で、0.83 秒の見失いのあいだ目と口を保持し、捉え直したときに跳ねない。生の値は跳ねている |
| | `LongFaceLossReturnsToTheFrontWithoutJumps` | 長く見失うと、跳ねずに正面へ戻る |
| | `OutOfRangeAnglesStopAtTheParameterLimit` | 実機の範囲外の値で、`AngleX` が ±30 で止まる |
| | `CalibrationOnRecordedDataMakesThatPoseFront` | 実機の記録で、校正した向きが正面になる |
| `LatencyMeterTests` | `ReturnsNullBeforeAnySample`、`ComputesAverageP95AndMaximumOfRecentSamples`、`KeepsOnlyTheMostRecentSamples`、`IgnoresInvalidSamples`、`RejectsNonPositiveCapacity` | 遅延の平均・p95・最大を直近の測定値から求め、負の値と NaN を捨てる |
| `ProbeRecorderTests` | `RecordsMessagesFramesAndParameters` | 確認ツールが、受け取ったメッセージ、モデルあり・なしの通知の数、届いたパラメータ名、パラメータ一覧、状態の変化、受信レートを記録する |
| | `KeepsUnreadableMessagesAsText` | JSON として読めないメッセージを文字列のまま記録する |

### Format tests

`ugokue-format validate` で `format/samples/minimal` と `format/samples/test-face`（[固定テストモデル](test-model.md)）を検証したあと、`format/python/tests/` を実行する。表の「各サンプル」のテストは、2つのフォルダのそれぞれで実行する。

| ファイル | テスト | 確かめること |
| --- | --- | --- |
| `test_roundtrip.py` | `test_sample_regenerates_byte_identically` | サンプルの生成結果が、リポジトリのサンプルとバイト単位で一致する |
| | `test_test_face_regenerates_byte_identically` | 固定テストモデルの生成結果が、リポジトリのフォルダとバイト単位で一致する |
| | `test_python_roundtrip`（各サンプル） | Python で読み込んで書き出すと、元と一致する |
| | `test_csharp_roundtrip`（各サンプル） | C#（`dotnet/Ugokue.Format.Cli`）で読み込んで書き出すと、元と一致する（JSON は値として、PNG はバイト列で比較）。CI では `UGOKUE_REQUIRE_DOTNET=1` を設定し、dotnet がなければ失敗させる |
| | `test_load_rejects_unreadable_version` | 読めないバージョンを拒否する |
| | `test_compare_reports_differences` | 比較ツールが値とテクスチャの違いを検出する |
| `test_validate.py` | `test_sample_is_valid` | サンプルが検証を通る |
| | `test_missing_required_field`、`test_wrong_type`、`test_unknown_field`、`test_confidence_out_of_range`、`test_bad_version` | 必須項目の欠け、型の違い、未定義の項目、範囲外の値、読めないバージョンを検出する |
| | `test_rule01_`〜`test_rule14_` で始まるテスト | [中間形式の検証規則](intermediate-format.md#検証) の 1〜14 を、それぞれ1つ以上の壊したサンプルで検出する |
| | `test_malformed_document_reports_without_crash` | 構造が大きく壊れたファイルでも、例外で止まらずにエラーを報告する |
| `test_face_model.py` | `test_test_face_is_valid` | 固定テストモデルが検証を通る |
| | `test_has_about_twenty_face_parts` | パーツが 20 以上あり、前髪・眉・まぶた・白目・黒目・口の中などの役割がそろっている |
| | `test_every_head_mesh_has_angle_grid` | 頭の子孫のメッシュがすべて、AngleX × AngleY の 3×3 のキーフォームを持つ |
| | `test_blink_and_mouth_move_their_meshes` | EyeOpenL、EyeOpenR、MouthOpenY のキーフォームが、目と口（あごを含む）のメッシュを動かす |
| | `test_default_parameters_give_base_shape` | パラメータが初期値のとき、どのキーフォームも移動量が 0 |
| | `test_views_cover_angle_grid` | ビューが顔の向きの 3×3 の格子と一致する |
| | `test_landmarks_exist_in_every_view_where_visible` | 特徴点が、隠れていない限りすべてのビューにある |
| | `test_vertex_matches_follow_angle_keyforms` | 頂点の対応の位置が、そのビューの角度のキーフォームを適用した頂点位置と一致する |

### Unity EditMode tests

| クラス | テスト | 確かめること |
| --- | --- | --- |
| `MainSceneTests` | `MainSceneIsTheOnlyEnabledBuildScene` | ビルドに含まれるシーンが Main シーンだけである |
| | `MainSceneHasCameraAndPerformanceMonitor` | Main シーンにカメラと `PerformanceMonitor` がある |
| | `FrameTimingStatsAreEnabled` | Player Settings の Frame Timing Stats がオン（CPU 時間の計測に必要） |
| | `PlayerKeepsRunningWhenUnfocused` | Player Settings の Run In Background がオン（OBS などにフォーカスがあっても止まらない） |
| | `MainSceneHasModelRendererWithPartMaterialAndSliders` | Main シーンに `ModelRenderer`（`Ugokue/UnlitTransparent` のマテリアル付き）と `ParameterSliders` がある |
| | `MainSceneModelRendererHasMaskAndDebugMaterials`（マスク用、デバッグ用） | `ModelRenderer` にマスク用（`Ugokue/StencilMask`）とデバッグ用（`Ugokue/DebugColor`）のマテリアルが設定されている |
| | `MainSceneHasDrawOrderDebugViewTargetingTheModel` | Main シーンに、モデルを対象にした `DrawOrderDebugView` がある |
| `NizimaLiveReceiverTests` | `MainSceneHasOneReceiverOnTheDefaultPort` | Main シーンに、ポート 22022 に接続する `NizimaLiveReceiver` が1つある |
| | `TokenIsKeptInPersistentData` | トークンを永続データフォルダに保存する |
| | `StatusShowsAHintUntilStreaming`（3通り） | 値が届くまでの各状態で、次にする操作の案内を表示する |
| | `StatusShowsRateWhileStreaming`、`StatusWarnsWhenFramesHaveNoModel` | 受信中は受信レートを、モデルのない通知のときはその旨を表示する |
| | `ValuesAreListedInIdOrder` | 受け取った値をパラメータ名の順に表示する |
| `TrackingDriverTests` | `MainSceneHasDriverWiredToTheModelAndTheReceiver` | Main シーンに、モデルと `NizimaLiveReceiver` につながった `TrackingDriver` がある |
| | `RecordedInputMovesTheTestFace` | 実機の記録を再生すると、固定テストモデルの `AngleX` がアダプターの出力どおりに動き、±30 で止まる |
| | `CalibrationMakesTheCurrentPoseFront` | 傾けた状態で校正すると、`AngleZ` が正面（±1°）になる |
| | `ParametersAreLeftAloneBeforeTheFirstFrame` | 入力が来るまで、スライダーなどで設定した値を上書きしない |
| | `ReadsThePlaybackArgument` | 起動引数 `-ugokue-tracking-playback` を読む |
| | `StatusDescribesTheState`（4通り）、`StatusShowsCalibrationAndLatency` | 状態、校正の有無、遅延を表示する |
| | `LatencyMarkerLightsWhenTheMouthIsOpen`（3通り） | 遅延計測用の目印は、口の開きが 0.5 を超えたときだけ点く |
| `ModelRendererTests` | `CreatesOneRendererPerPartWithMeshPlusMaskAndClippedAwayRenderers` | メッシュを持つパーツごとに1つ、マスクごとに1つ、切り抜かれるパーツごとにデバッグ用の1つの MeshRenderer を作る（`head` には作らない） |
| | `VerticesFollowRigPositionsAfterTurningRight` | AngleX = 30 で、Unity のメッシュの頂点が変形後の位置を座標変換したものと一致し、隠れる耳が非表示になる |
| | `SortingOrderFollowsDrawListWithMasksBeforeTheirPart`（AngleX = 0、30、−30） | `sortingOrder` が描画リストの順で、マスクがそのパーツの直前に来る。表示するのは描画リストのパーツとそのマスクだけ |
| | `ClippedPartsHaveOneMaskRendererUsingTheMaskPartMesh` | 黒目と歯がそれぞれマスクを1つ持ち、マスクのパーツと同じ Unity の Mesh と `Ugokue/StencilMask` を使う |
| | `MaskVerticesFollowTheMaskPartAfterMoving` | パラメータを変えると、マスクの頂点もマスクのパーツと同じように動く |
| | `StencilValuesOfClippedPartsDoNotOverlap` | マスクは Always / Replace、パーツは Equal で同じ値を使い、値はパーツごとに重ならない |
| | `UnclippedPartsDoNotUseTheStencil` | 切り抜かないパーツはステンシルを使わない |
| | `DebugViewColorsPartsByRankAndShowsClippedAwayShapes` | デバッグ表示で、各パーツが描画リストの順番の色になり、切り抜かれる部分がマゼンタで表示される。戻すと元のマテリアルに戻る |
| | `UvsAreFlippedToBottomLeftOrigin` | UV の v が 1 − v になる |
| `ModelLocationTests` | `ArgumentWinsOverDefaults`、`EditorDefaultsToRepositoryTestFace`、`PlayerDefaultsToStreamingAssets`、`ArgumentWithoutValueFallsBackToDefault` | 読み込むモデルのフォルダを、起動引数、エディター、プレイヤーの順に決める |
| `StreamingModelBuildHookTests` | `CopyModelCopiesModelJsonAndAllPngs` | ビルド前に model.json とすべての PNG を StreamingAssets に複製する |
| | `RemoveModelDeletesCopyMetaFilesAndEmptyFolders`、`RemoveModelKeepsOtherStreamingAssets` | ビルド後に複製と .meta、空になったフォルダを消し、ほかのファイルは残す |
| `FormatSampleTests` | `SampleLoadsWithPngTextures`（`minimal`、`test-face`） | Unity 上でサンプルを読み込め、すべてのテクスチャが PNG として読める |
| | `SampleRoundTripsThroughSave`（`minimal`、`test-face`） | Unity 上で読み込み → 書き出し → 読み込みをしても、内容が Python で書いた元と値として一致する |

### Unity build

- Windows と Mac のプレイヤーがエラーなくビルドできる。

### Launch and record

`.github/scripts/perf_smoke.py` が、プレイヤーを `-batchmode -nographics` で起動して 3 秒記録し、次を確かめる。描画しないため、fps の値は [数値目標](performance-targets.md) の判定には使わない。

- プレイヤーが記録を保存して自分で終了する（180 秒でタイムアウト）
- 本体 CSV のヘッダーが仕様どおり
- フレーム番号が 0 から連番で、時刻が減らず、フレーム時間が負でない
- 最初の時刻が 0 で、最後の時刻が指定秒数以上
- 集計 CSV のフレーム数が本体 CSV の行数と一致し、記録時間が指定秒数以上

## チケットのテスト項目との対応

| チケット | テスト項目 | CI での確認 |
| --- | --- | --- |
| PoC-01 #3 | 自動ビルドが通る | Unity build |
| PoC-01 #3 | 計測の記録が、指定した秒数ぶん欠けなく保存される | `FrameRecorderTests.RecordsEveryFrameForTheRequestedDuration`、Launch and record |
| PoC-02 #4 | Python で書き出し → C# で読み込み → 再び書き出し、元と一致する | Format tests の `test_csharp_roundtrip`、Unity EditMode tests の `FormatSampleTests` |
| PoC-02 #4 | 必須項目の欠けや不正な値を、検証ツールが検出する | Format tests の `test_validate.py` |
| PoC-03 #5 | 検証ツールでエラーが出ない | Format tests の「Validate samples」と `test_test_face_is_valid` |
| PoC-03 #5 | 以降のチケットの自動テストで、常に同じ入力として使える | Format tests の `test_test_face_regenerates_byte_identically`、`test_face_model.py`、各サンプルの往復、Unity EditMode tests の `FormatSampleTests` |
| PoC-04 #6 | 補間計算の単体テスト（端の値、中間値、範囲外の値） | .NET tests の `KeyformInterpolatorTests`、`ModelRigTests` |
| PoC-04 #6 | 固定のパラメータ値で描画した画像を、基準画像と比較する | .NET tests の `RenderRegressionTests` |
| PoC-04 #6 | キーフォームの位置では、作成した形と一致する（受け入れ条件） | .NET tests の `MatchesAuthoredShapeAtKeyformPoints`、`FaceMatchesVertexCorrespondenceInEveryView`、Unity EditMode tests の `VerticesFollowRigPositionsAfterTurningRight` |
| PoC-05 #7 | 角度スイープ（正面 → ±30°）で画像回帰テストを行い、ちらつきや二重像を検出する | .NET tests の `RenderMatchesBaseline`（`sweep-*`）、`OcclusionTests`（`ViewSwitchIsSeamlessAtBoundary` を含む） |
| PoC-05 #7 | 黒目が白目からはみ出さない（受け入れ条件） | .NET tests の `ClippedPartsStayInsideTheirMasksDuringSweep`、`IrisWouldStickOutWithoutClipping` |
| PoC-05 #7 | ±30° の範囲で、前髪と顔の前後関係が崩れない（受け入れ条件） | .NET tests の `FrontHairStaysInFrontOfFaceDuringSweep` |
| PoC-06 #9 | 届いた JSON を読み取る処理の単体テスト | .NET tests の `RecordedNizimaMessageTests`（実機で記録したデータ）、`NizimaProtocolTests`、`NizimaLiveClientTests` |
| PoC-06 #9 | nizima LIVE を終了・再起動しても、アプリが落ちずに再接続できる | .NET tests の `ReconnectsAfterNizimaLiveRestarts`、`KeepsRetryingUntilNizimaLiveStarts` |
| PoC-07 #10 | 記録した入力データを再生するテスト（校正・平滑化・途切れ・範囲外の値） | .NET tests の `TrackingPlaybackTests`、`TrackingAdapterTests`、Unity EditMode tests の `TrackingDriverTests` |
| PoC-07 #10 | 総遅延を計測して記録する | アプリ内の遅延表示は `LatencyMeterTests`。全体の遅延は人がスローモーション撮影で測る（[入力アダプター](tracking-adapter.md#遅延)） |

## 成果物

| 名前 | 内容 |
| --- | --- |
| `render-regression` | 画像回帰テストが失敗したときの実際の画像と違いの画像（失敗時だけ） |
| `unity-test-results` | EditMode テストの結果 XML とログ |
| `ugokue-StandaloneWindows64`、`ugokue-StandaloneOSX` | ビルドしたプレイヤー（tar.gz） |
| `perf-windows-latest`、`perf-macos-latest` | 起動確認で記録した CSV |
