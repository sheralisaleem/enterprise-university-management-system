import 'package:flutter_test/flutter_test.dart';
import 'package:event_management_mobile/main.dart';

void main() {
  testWidgets('Login screen renders brand', (WidgetTester tester) async {
    await tester.pumpWidget(const FypApp());
    expect(find.text('FYP Events'), findsOneWidget);
    expect(find.text('Sign in'), findsOneWidget);
  });
}
