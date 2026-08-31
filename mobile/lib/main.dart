import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:shared_preferences/shared_preferences.dart';

const apiBaseUrl = String.fromEnvironment(
  'API_BASE',
  defaultValue: 'http://10.0.2.2:5287',
);

void main() {
  runApp(const FypApp());
}

class FypApp extends StatelessWidget {
  const FypApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'FYP Events',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF0F6A4C),
          brightness: Brightness.light,
        ),
        useMaterial3: true,
        fontFamily: 'Roboto',
      ),
      home: const LoginPage(),
    );
  }
}

class ApiClient {
  ApiClient(this.token)
      : dio = Dio(BaseOptions(
          baseUrl: apiBaseUrl,
          headers: {
            if (token != null && token.isNotEmpty)
              'Authorization': 'Bearer $token',
          },
        ));

  final String? token;
  final Dio dio;

  Future<Map<String, dynamic>> login(String email, String password) async {
    final res = await dio.post('/api/auth/login', data: {
      'email': email,
      'password': password,
    });
    return Map<String, dynamic>.from(res.data as Map);
  }

  Future<List<dynamic>> myItinerary() async {
    final res = await dio.get('/api/allocations/mine');
    return res.data as List<dynamic>;
  }

  Future<List<dynamic>> projects() async {
    final res = await dio.get('/api/projects');
    return res.data as List<dynamic>;
  }

  Future<void> uploadDocument(int projectId, String path, String name) async {
    final form = FormData.fromMap({
      'documentType': 'Proposal',
      'file': await MultipartFile.fromFile(path, filename: name),
    });
    await dio.post('/api/projects/$projectId/documents', data: form);
  }

  Future<Map<String, dynamic>> rubric(int eventId) async {
    final res = await dio.get('/api/grades/rubric/$eventId');
    return Map<String, dynamic>.from(res.data as Map);
  }

  Future<void> submitScores(int allocationId, List<Map<String, dynamic>> scores) async {
    await dio.post('/api/grades/scores', data: {
      'roomAllocationId': allocationId,
      'scores': scores,
    });
  }

  Future<void> setRoomStatus(int allocationId, String status) async {
    await dio.patch('/api/allocations/$allocationId/status', data: {
      'roomStatus': status,
    });
  }
}

class LoginPage extends StatefulWidget {
  const LoginPage({super.key});

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final emailCtrl = TextEditingController(text: 'student@fyp.local');
  final passCtrl = TextEditingController(text: 'Student@123');
  String? error;
  bool loading = false;

  Future<void> _login() async {
    setState(() {
      loading = true;
      error = null;
    });
    try {
      final api = ApiClient(null);
      final auth = await api.login(emailCtrl.text.trim(), passCtrl.text);
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString('token', auth['token'] as String);
      await prefs.setString('role', auth['role'] as String);
      await prefs.setString('name', auth['fullName'] as String);
      if (!mounted) return;
      Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => HomeShell(auth: auth)),
      );
    } catch (e) {
      setState(() => error = 'Login failed. Check API URL ($apiBaseUrl).');
    } finally {
      if (mounted) setState(() => loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Container(
        decoration: const BoxDecoration(
          gradient: LinearGradient(
            colors: [Color(0xFF0F6A4C), Color(0xFF14201B)],
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
          ),
        ),
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Card(
              margin: const EdgeInsets.all(20),
              child: Padding(
                padding: const EdgeInsets.all(20),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text('FYP Events',
                        style: Theme.of(context).textTheme.headlineMedium),
                    const SizedBox(height: 8),
                    const Text('Students & evaluators — itinerary and rubrics'),
                    const SizedBox(height: 16),
                    TextField(
                      controller: emailCtrl,
                      decoration: const InputDecoration(labelText: 'Email'),
                    ),
                    TextField(
                      controller: passCtrl,
                      obscureText: true,
                      decoration: const InputDecoration(labelText: 'Password'),
                    ),
                    if (error != null) ...[
                      const SizedBox(height: 8),
                      Text(error!, style: const TextStyle(color: Colors.red)),
                    ],
                    const SizedBox(height: 16),
                    FilledButton(
                      onPressed: loading ? null : _login,
                      child: Text(loading ? 'Signing in…' : 'Sign in'),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class HomeShell extends StatefulWidget {
  const HomeShell({super.key, required this.auth});
  final Map<String, dynamic> auth;

  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> {
  int index = 0;

  @override
  Widget build(BuildContext context) {
    final role = widget.auth['role'] as String? ?? '';
    final token = widget.auth['token'] as String;
    final pages = [
      ItineraryPage(token: token),
      DocumentsPage(token: token),
      if (role == 'Evaluator' || role == 'Admin') RubricPage(token: token),
    ];
    final destinations = [
      const NavigationDestination(icon: Icon(Icons.schedule), label: 'Itinerary'),
      const NavigationDestination(icon: Icon(Icons.upload_file), label: 'Docs'),
      if (role == 'Evaluator' || role == 'Admin')
        const NavigationDestination(icon: Icon(Icons.grade), label: 'Rubric'),
    ];

    return Scaffold(
      appBar: AppBar(
        title: Text('Hello, ${widget.auth['fullName']}'),
        actions: [
          IconButton(
            onPressed: () async {
              final prefs = await SharedPreferences.getInstance();
              await prefs.clear();
              if (!context.mounted) return;
              Navigator.of(context).pushReplacement(
                MaterialPageRoute(builder: (_) => const LoginPage()),
              );
            },
            icon: const Icon(Icons.logout),
          )
        ],
      ),
      body: pages[index.clamp(0, pages.length - 1)],
      bottomNavigationBar: NavigationBar(
        selectedIndex: index.clamp(0, destinations.length - 1),
        onDestinationSelected: (i) => setState(() => index = i),
        destinations: destinations,
      ),
    );
  }
}

class ItineraryPage extends StatefulWidget {
  const ItineraryPage({super.key, required this.token});
  final String token;

  @override
  State<ItineraryPage> createState() => _ItineraryPageState();
}

class _ItineraryPageState extends State<ItineraryPage> {
  late Future<List<dynamic>> future;

  @override
  void initState() {
    super.initState();
    future = ApiClient(widget.token).myItinerary();
  }

  @override
  Widget build(BuildContext context) {
    final fmt = DateFormat('EEE d MMM · HH:mm');
    return FutureBuilder(
      future: future,
      builder: (context, snap) {
        if (snap.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        if (snap.hasError) {
          return Center(child: Text('Could not load itinerary.\n$apiBaseUrl'));
        }
        final items = snap.data ?? [];
        if (items.isEmpty) {
          return const Center(child: Text('No assignments yet.'));
        }
        return ListView.builder(
          padding: const EdgeInsets.all(12),
          itemCount: items.length,
          itemBuilder: (context, i) {
            final a = items[i] as Map<String, dynamic>;
            return Card(
              child: ListTile(
                title: Text(a['projectTitle']?.toString() ?? 'Project'),
                subtitle: Text(
                  '${a['roomName']} · ${a['evaluatorName'] ?? 'TBA'}\n'
                  '${fmt.format(DateTime.parse(a['startTime']))} – '
                  '${fmt.format(DateTime.parse(a['endTime']))}\n'
                  'Status: ${a['roomStatus']}',
                ),
                isThreeLine: true,
                trailing: PopupMenuButton<String>(
                  onSelected: (status) async {
                    await ApiClient(widget.token)
                        .setRoomStatus(a['id'] as int, status);
                    setState(() {
                      future = ApiClient(widget.token).myItinerary();
                    });
                  },
                  itemBuilder: (_) => const [
                    PopupMenuItem(value: 'Occupied', child: Text('Mark Occupied')),
                    PopupMenuItem(value: 'Available', child: Text('Mark Available')),
                  ],
                ),
              ),
            );
          },
        );
      },
    );
  }
}

class DocumentsPage extends StatefulWidget {
  const DocumentsPage({super.key, required this.token});
  final String token;

  @override
  State<DocumentsPage> createState() => _DocumentsPageState();
}

class _DocumentsPageState extends State<DocumentsPage> {
  late Future<List<dynamic>> future;

  @override
  void initState() {
    super.initState();
    future = ApiClient(widget.token).projects();
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder(
      future: future,
      builder: (context, snap) {
        if (snap.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        if (snap.hasError) {
          return const Center(child: Text('Could not load projects.'));
        }
        final items = snap.data ?? [];
        return ListView.builder(
          padding: const EdgeInsets.all(12),
          itemCount: items.length,
          itemBuilder: (context, i) {
            final p = items[i] as Map<String, dynamic>;
            return Card(
              child: ListTile(
                title: Text(p['title']?.toString() ?? 'Project'),
                subtitle: Text('Domain: ${p['domain']} · ${p['readinessStatus']}'),
              ),
            );
          },
        );
      },
    );
  }
}

class RubricPage extends StatefulWidget {
  const RubricPage({super.key, required this.token});
  final String token;

  @override
  State<RubricPage> createState() => _RubricPageState();
}

class _RubricPageState extends State<RubricPage> {
  late Future<List<dynamic>> future;
  final scores = <int, TextEditingController>{};
  int? selectedAllocationId;
  int? selectedEventId;

  @override
  void initState() {
    super.initState();
    future = ApiClient(widget.token).myItinerary();
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder(
      future: future,
      builder: (context, snap) {
        if (snap.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        final items = snap.data ?? [];
        return ListView(
          padding: const EdgeInsets.all(12),
          children: [
            const Text('Select a slot, then submit criterion scores.'),
            const SizedBox(height: 8),
            ...items.map((raw) {
              final a = raw as Map<String, dynamic>;
              final id = a['id'] as int;
              return RadioListTile<int>(
                value: id,
                groupValue: selectedAllocationId,
                title: Text(a['projectTitle']?.toString() ?? ''),
                subtitle: Text('Room ${a['roomName']}'),
                onChanged: (v) async {
                  setState(() {
                    selectedAllocationId = v;
                    selectedEventId = a['eventId'] as int?;
                  });
                  if (selectedEventId != null) {
                    final rubric =
                        await ApiClient(widget.token).rubric(selectedEventId!);
                    final criteria = rubric['criteria'] as List<dynamic>? ?? [];
                    scores.clear();
                    for (final c in criteria) {
                      final map = c as Map<String, dynamic>;
                      scores[map['id'] as int] = TextEditingController();
                    }
                    setState(() {});
                  }
                },
              );
            }),
            ...scores.entries.map(
              (e) => Padding(
                padding: const EdgeInsets.symmetric(vertical: 4),
                child: TextField(
                  controller: e.value,
                  keyboardType: TextInputType.number,
                  decoration: InputDecoration(
                    labelText: 'Criterion #${e.key} score',
                    border: const OutlineInputBorder(),
                  ),
                ),
              ),
            ),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: selectedAllocationId == null || scores.isEmpty
                  ? null
                  : () async {
                      final payload = scores.entries
                          .map((e) => {
                                'rubricCriterionId': e.key,
                                'score': double.tryParse(e.value.text) ?? 0,
                                'comments': null,
                              })
                          .toList();
                      await ApiClient(widget.token)
                          .submitScores(selectedAllocationId!, payload);
                      if (!context.mounted) return;
                      ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(content: Text('Scores submitted')),
                      );
                    },
              child: const Text('Submit rubric'),
            ),
          ],
        );
      },
    );
  }
}
