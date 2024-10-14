<script lang="ts">
	import '../app.postcss';
	import { AppShell, AppBar } from '@skeletonlabs/skeleton';
</script>

<!-- App Shell -->
<AppShell>
	<svelte:fragment slot="header">
		<!-- App Bar -->
		<AppBar>
			<svelte:fragment slot="lead">
				<img src="/icon.png" alt="Moirai" class="w-8 h-8 mr-2" />
				<strong class="text-xl uppercase">Moirai</strong>
			</svelte:fragment>
			<svelte:fragment slot="trail">
				<a href="/records">Records</a>
				<a href="/records">Changesets</a>
			</svelte:fragment>
		</AppBar>
	</svelte:fragment>
	<!-- Page Route Content -->
	<slot />
</AppShell>
