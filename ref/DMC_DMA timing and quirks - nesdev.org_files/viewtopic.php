<!DOCTYPE html>
<html dir="ltr" lang="en-gb">
<head>
<meta charset="utf-8" />
<meta http-equiv="X-UA-Compatible" content="IE=edge">
<meta name="viewport" content="width=device-width, initial-scale=1" />

<title>DMC/DMA timing and quirks - nesdev.org</title>

	<link rel="alternate" type="application/atom+xml" title="Feed - nesdev.org" href="/app.php/feed?sid=5673b41f701d34d4a2afb3e98bb483e5">	<link rel="alternate" type="application/atom+xml" title="Feed - News" href="/app.php/feed/news?sid=5673b41f701d34d4a2afb3e98bb483e5">	<link rel="alternate" type="application/atom+xml" title="Feed - All forums" href="/app.php/feed/forums?sid=5673b41f701d34d4a2afb3e98bb483e5">	<link rel="alternate" type="application/atom+xml" title="Feed - New Topics" href="/app.php/feed/topics?sid=5673b41f701d34d4a2afb3e98bb483e5">	<link rel="alternate" type="application/atom+xml" title="Feed - Active Topics" href="/app.php/feed/topics_active?sid=5673b41f701d34d4a2afb3e98bb483e5">	<link rel="alternate" type="application/atom+xml" title="Feed - Forum - NESemdev" href="/app.php/feed/forum/3?sid=5673b41f701d34d4a2afb3e98bb483e5">	<link rel="alternate" type="application/atom+xml" title="Feed - Topic - DMC/DMA timing and quirks" href="/app.php/feed/topic/25574?sid=5673b41f701d34d4a2afb3e98bb483e5">	
	<link rel="canonical" href="https://forums.nesdev.org/viewtopic.php?t=25574">

<!--
	phpBB style name: Prosilver (Dark Edition)
	Based on style:   prosilver (this is the default phpBB3 style)
	Original author:  Tom Beddard ( http://www.subBlue.com/ )
	Modified by: PlanetStyles.net ( http://www.planetstyles.net)
-->

<link href="./assets/css/font-awesome.min.css?assets_version=61" rel="stylesheet">
<link href="./styles/prosilver_dark/theme/stylesheet.css?assets_version=61" rel="stylesheet">
<link href="./styles/prosilver_dark/theme/en/stylesheet.css?assets_version=61" rel="stylesheet">




<!--[if lte IE 9]>
	<link href="./styles/prosilver_dark/theme/tweaks.css?assets_version=61" rel="stylesheet">
<![endif]-->



<link href="./styles/prosilver_dark/theme/colours/orange.css?assets_version=61" rel="stylesheet">





</head>
<body id="phpbb" class="nojs notouch section-viewtopic ltr ">


<div id="wrap" class="wrap">
	<a id="top" class="top-anchor" accesskey="t"></a>
	<div id="page-header">
		<div class="headerbar" role="banner">
					<div class="inner">

			<div id="site-description" class="site-description">
				<a id="logo" class="logo" href="./index.php?sid=5673b41f701d34d4a2afb3e98bb483e5" title="Board index"><span class="site_logo"></span></a>
				<!-- <h1>nesdev.org</h1>
				<p>NES Development Forums</p> -->
				<p class="skiplink"><a href="#start_here">Skip to content</a></p>
			</div>

									<div id="search-box" class="search-box search-header" role="search">
				<form action="./search.php?sid=5673b41f701d34d4a2afb3e98bb483e5" method="get" id="search">
				<fieldset>
					<input name="keywords" id="keywords" type="search" maxlength="128" title="Search for keywords" class="inputbox search tiny" size="20" value="" placeholder="Search…" />
					<button class="button button-search" type="submit" title="Search">
						<i class="icon fa-search fa-fw" aria-hidden="true"></i><span class="sr-only">Search</span>
					</button>
					<a href="./search.php?sid=5673b41f701d34d4a2afb3e98bb483e5" class="button button-search-end" title="Advanced search">
						<i class="icon fa-cog fa-fw" aria-hidden="true"></i><span class="sr-only">Advanced search</span>
					</a>
					<input type="hidden" name="sid" value="5673b41f701d34d4a2afb3e98bb483e5" />

				</fieldset>
				</form>
			</div>
						
			</div>
					</div>
				<div class="navbar" role="navigation">
	<div class="inner">

	<ul id="nav-main" class="nav-main linklist" role="menubar">

		<li id="quick-links" class="quick-links dropdown-container responsive-menu" data-skip-responsive="true">
			<a href="#" class="dropdown-trigger">
				<i class="icon fa-bars fa-fw" aria-hidden="true"></i><span>Quick links</span>
			</a>
			<div class="dropdown">
				<div class="pointer"><div class="pointer-inner"></div></div>
				<ul class="dropdown-contents" role="menu">
					
											<li class="separator"></li>
																									<li>
								<a href="./search.php?search_id=unanswered&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" role="menuitem">
									<i class="icon fa-file-o fa-fw icon-gray" aria-hidden="true"></i><span>Unanswered topics</span>
								</a>
							</li>
							<li>
								<a href="./search.php?search_id=active_topics&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" role="menuitem">
									<i class="icon fa-file-o fa-fw icon-blue" aria-hidden="true"></i><span>Active topics</span>
								</a>
							</li>
							<li class="separator"></li>
							<li>
								<a href="./search.php?sid=5673b41f701d34d4a2afb3e98bb483e5" role="menuitem">
									<i class="icon fa-search fa-fw" aria-hidden="true"></i><span>Search</span>
								</a>
							</li>
					
										<li class="separator"></li>

									</ul>
			</div>
		</li>

				<li data-skip-responsive="true">
			<a href="/app.php/help/faq?sid=5673b41f701d34d4a2afb3e98bb483e5" rel="help" title="Frequently Asked Questions" role="menuitem">
				<i class="icon fa-question-circle fa-fw" aria-hidden="true"></i><span>FAQ</span>
			</a>
		</li>
						
			<li class="rightside"  data-skip-responsive="true">
			<a href="./ucp.php?mode=login&amp;redirect=viewtopic.php%3Fp%3D297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" title="Login" accesskey="x" role="menuitem">
				<i class="icon fa-power-off fa-fw" aria-hidden="true"></i><span>Login</span>
			</a>
		</li>
					<li class="rightside" data-skip-responsive="true">
				<a href="./ucp.php?mode=register&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" role="menuitem">
					<i class="icon fa-pencil-square-o  fa-fw" aria-hidden="true"></i><span>Register</span>
				</a>
			</li>
						</ul>

	<ul id="nav-breadcrumbs" class="nav-breadcrumbs linklist navlinks" role="menubar">
				
		
		<li class="breadcrumbs" itemscope itemtype="https://schema.org/BreadcrumbList">

			
							<span class="crumb" itemtype="https://schema.org/ListItem" itemprop="itemListElement" itemscope><a itemprop="item" href="./index.php?sid=5673b41f701d34d4a2afb3e98bb483e5" accesskey="h" data-navbar-reference="index"><i class="icon fa-home fa-fw"></i><span itemprop="name">Board index</span></a><meta itemprop="position" content="1" /></span>

											
								<span class="crumb" itemtype="https://schema.org/ListItem" itemprop="itemListElement" itemscope data-forum-id="24"><a itemprop="item" href="./viewforum.php?f=24&amp;sid=5673b41f701d34d4a2afb3e98bb483e5"><span itemprop="name">NES / Famicom</span></a><meta itemprop="position" content="2" /></span>
															
								<span class="crumb" itemtype="https://schema.org/ListItem" itemprop="itemListElement" itemscope data-forum-id="3"><a itemprop="item" href="./viewforum.php?f=3&amp;sid=5673b41f701d34d4a2afb3e98bb483e5"><span itemprop="name">NESemdev</span></a><meta itemprop="position" content="3" /></span>
							
					</li>

		
					<li class="rightside responsive-search">
				<a href="./search.php?sid=5673b41f701d34d4a2afb3e98bb483e5" title="View the advanced search options" role="menuitem">
					<i class="icon fa-search fa-fw" aria-hidden="true"></i><span class="sr-only">Search</span>
				</a>
			</li>
			</ul>

	</div>
</div>
	</div>

	
	<a id="start_here" class="anchor"></a>
	<div id="page-body" class="page-body" role="main">
		
		
<h2 class="topic-title"><a href="./viewtopic.php?t=25574&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">DMC/DMA timing and quirks</a></h2>
<!-- NOTE: remove the style="display: none" when you want to have the forum description on the topic body -->
<div style="display: none !important;">Discuss emulation of the Nintendo Entertainment System and Famicom.<br /></div>


<div class="action-bar bar-top">
	
			<a href="./posting.php?mode=reply&amp;t=25574&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="button" title="Post a reply">
							<span>Post Reply</span> <i class="icon fa-reply fa-fw" aria-hidden="true"></i>
					</a>
	
			<div class="dropdown-container dropdown-button-control topic-tools">
		<span title="Topic tools" class="button button-secondary dropdown-trigger dropdown-select">
			<i class="icon fa-wrench fa-fw" aria-hidden="true"></i>
			<span class="caret"><i class="icon fa-sort-down fa-fw" aria-hidden="true"></i></span>
		</span>
		<div class="dropdown">
			<div class="pointer"><div class="pointer-inner"></div></div>
			<ul class="dropdown-contents">
																												<li>
					<a href="./viewtopic.php?t=25574&amp;sid=5673b41f701d34d4a2afb3e98bb483e5&amp;view=print" title="Print view" accesskey="p">
						<i class="icon fa-print fa-fw" aria-hidden="true"></i><span>Print view</span>
					</a>
				</li>
											</ul>
		</div>
	</div>
	
			<div class="search-box" role="search">
			<form method="get" id="topic-search" action="./search.php?sid=5673b41f701d34d4a2afb3e98bb483e5">
			<fieldset>
				<input class="inputbox search tiny"  type="search" name="keywords" id="search_keywords" size="20" placeholder="Search this topic…" />
				<button class="button button-search" type="submit" title="Search">
					<i class="icon fa-search fa-fw" aria-hidden="true"></i><span class="sr-only">Search</span>
				</button>
				<a href="./search.php?sid=5673b41f701d34d4a2afb3e98bb483e5" class="button button-search-end" title="Advanced search">
					<i class="icon fa-cog fa-fw" aria-hidden="true"></i><span class="sr-only">Advanced search</span>
				</a>
				<input type="hidden" name="t" value="25574" />
<input type="hidden" name="sf" value="msgonly" />
<input type="hidden" name="sid" value="5673b41f701d34d4a2afb3e98bb483e5" />

			</fieldset>
			</form>
		</div>
	
			<div class="pagination">
			4 posts
							&bull; Page <strong>1</strong> of <strong>1</strong>
					</div>
		</div>




			<div id="p297120" class="post has-profile bg2">
		<div class="inner">

		<dl class="postprofile" id="profile297120">
			<dt class="no-profile-rank no-avatar">
				<div class="avatar-container">
																			</div>
								<a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="username">EvtimDjerekarov</a>							</dt>

									
		<dd class="profile-posts"><strong>Posts:</strong> <a href="./search.php?author_id=3511&amp;sr=posts&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">9</a></dd>		<dd class="profile-joined"><strong>Joined:</strong> Sun Apr 13, 2008 5:58 am</dd>		
		
						
						
		</dl>

		<div class="postbody">
						<div id="post_content297120">

					<h3 class="first">
						<a href="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120">DMC/DMA timing and quirks</a>
		</h3>

													<ul class="post-buttons">
																																									<li>
							<a href="./posting.php?mode=quote&amp;p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" title="Reply with quote" class="button button-icon-only">
								<i class="icon fa-quote-left fa-fw" aria-hidden="true"></i><span class="sr-only">Quote</span>
							</a>
						</li>
														</ul>
							
						<p class="author">
									<a class="unread" href="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120" title="Post">
						<i class="icon fa-file fa-fw icon-lightgray icon-md" aria-hidden="true"></i><span class="sr-only">Post</span>
					</a>
								<span class="responsive-hide">by <strong><a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="username">EvtimDjerekarov</a></strong> &raquo; </span><time datetime="2024-11-12T10:43:23+00:00">Tue Nov 12, 2024 3:43 am</time>
			</p>
			
			
			
			
			<div class="content">Hello to everyone,<br>
<br>
I have been reading about DMC and its DMA on the wiki and trying to implement it with mixed results (and feelings  <img class="smilies" src="./images/smilies/icon_lol.gif" width="15" height="15" alt=":lol:" title="Laughing"> ).<br>
Putting for now aside the exact way DMA steals CPU cycles, i have some very strange results (the "DMC DMA during OAM DMA" test ROM shows results like 4000 cycles in one case and 114 in other cases, the correct being about 514+, while debugging my emulator clearly shows that the OAM dma takes about 513 cycles at the time the last OAM DMA byte is fetched). Which all seems very odd to me. Ill dig into the rest ROM source to better understand it. <br>
<br>
I have some questions about the DMC playback in general.<br>
<br>
1. It looks like the sample length, if not set, is always 1. Is this correct.<br>
2. If sample address is not set, is 0xC000 assumed?<br>
3. If TimerPeriod is not set, is 0 assumed (428 CPU ccs)?<br>
4. How does the current sample address wrap around when incremented?<br>
<br>
5. We know that there are two 1-byte registers holding sample data during DMC playback - the <strong class="text-strong">ReaderBuffer</strong> and the <strong class="text-strong">ShiftRegister</strong>.<br>
<br>
Whenever the <strong class="text-strong">ShiftRegister</strong> shifts out (at the rate of CPUSpeed / TimerPeriod) all of its 8 bits to the DAC counter, the <strong class="text-strong">ReaderBuffer</strong> is copied into it and if <strong class="text-strong">BytesRemaing</strong> is more than 0, a new DMA cycle begins, to fetch a new sample into the <strong class="text-strong">ReaderBuffer</strong>. <br>
If there are no more samples and looping is disabled, IRQ flag is raised if DMC IRQ is enabled.<br>
<br>
There are many questions and edge cases with this:<br>
- if looping is disabled, is IRQ raised when the last sample bit is shifted out to the DAC counter (all 8 bits are shifted out and <strong class="text-strong">ReaderBuffer</strong> is empty)?<br>
- If looping is enabled, when does the first DMA of the second sample repetition begin (will there be a one DMA worth of time "gap" between repetitions)?<br>
- what happens to the <strong class="text-strong">ReaderBuffer</strong> and <strong class="text-strong">ShiftRegister</strong> if playback is stopped in the middle of a sample? Can it be resumed or it can only be restarted?<br>
- What means to "restart" the current sample. A new DMA to fetch its first byte will begin as soon as <strong class="text-strong">ShiftRegister</strong> has <strong class="text-strong">BitsRemaining = 0</strong> and <strong class="text-strong">ReaderBuffer</strong> is empty?</div>

			
			
									
						
										
						</div>

		</div>

				<div class="back2top">
						<a href="#top" class="top" title="Top">
				<i class="icon fa-chevron-circle-up fa-fw icon-gray" aria-hidden="true"></i>
				<span class="sr-only">Top</span>
			</a>
					</div>
		
		</div>
	</div>

				<div id="p297121" class="post has-profile bg1">
		<div class="inner">

		<dl class="postprofile" id="profile297121">
			<dt class="has-profile-rank no-avatar">
				<div class="avatar-container">
																			</div>
								<a href="./memberlist.php?mode=viewprofile&amp;u=8835&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="username">Fiskbit</a>							</dt>

						<dd class="profile-rank">Site Admin</dd>			
		<dd class="profile-posts"><strong>Posts:</strong> <a href="./search.php?author_id=8835&amp;sr=posts&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">1442</a></dd>		<dd class="profile-joined"><strong>Joined:</strong> Sat Nov 18, 2017 9:15 pm</dd>		
		
						
						
		</dl>

		<div class="postbody">
						<div id="post_content297121">

					<h3 >
						<a href="./viewtopic.php?p=297121&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297121">Re: DMC/DMA timing and quirks</a>
		</h3>

													<ul class="post-buttons">
																																									<li>
							<a href="./posting.php?mode=quote&amp;p=297121&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" title="Reply with quote" class="button button-icon-only">
								<i class="icon fa-quote-left fa-fw" aria-hidden="true"></i><span class="sr-only">Quote</span>
							</a>
						</li>
														</ul>
							
						<p class="author">
									<a class="unread" href="./viewtopic.php?p=297121&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297121" title="Post">
						<i class="icon fa-file fa-fw icon-lightgray icon-md" aria-hidden="true"></i><span class="sr-only">Post</span>
					</a>
								<span class="responsive-hide">by <strong><a href="./memberlist.php?mode=viewprofile&amp;u=8835&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="username">Fiskbit</a></strong> &raquo; </span><time datetime="2024-11-12T12:30:43+00:00">Tue Nov 12, 2024 5:30 am</time>
			</p>
			
			
			
			
			<div class="content"><blockquote cite="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120"><div><cite><a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">EvtimDjerekarov</a> wrote: <a href="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120" aria-label="View quoted post" data-post-id="297120" onclick="if(document.getElementById(hash.substr(1)))href=hash"><i class="icon fa-arrow-circle-up fa-fw" aria-hidden="true"></i></a><span class="responsive-hide">Tue Nov 12, 2024 3:43 am</span></cite>
I have been reading about DMC and its DMA on the wiki and trying to implement it with mixed results (and feelings <img class="smilies" src="./images/smilies/icon_lol.gif" width="15" height="15" alt=":lol:" title="Laughing"> ).
</div></blockquote>

I wrote the wiki's DMA article and am happy to answer any questions about it and improve the article as needed. It's a challenging topic for people.<br>

<blockquote cite="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120"><div><cite><a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">EvtimDjerekarov</a> wrote: <a href="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120" aria-label="View quoted post" data-post-id="297120" onclick="if(document.getElementById(hash.substr(1)))href=hash"><i class="icon fa-arrow-circle-up fa-fw" aria-hidden="true"></i></a><span class="responsive-hide">Tue Nov 12, 2024 3:43 am</span></cite>
1. It looks like the sample length, if not set, is always 1. Is this correct.<br>
2. If sample address is not set, is 0xC000 assumed?<br>
3. If TimerPeriod is not set, is 0 assumed (428 CPU ccs)?
</div></blockquote>

None of these registers have a defined power-on value. If the CPU has been powered off for some time, you can expect these registers to hold a 0 value. If the CPU was only recently powered off, some or all bits may not yet have decayed to 0. On reset, they don't change. See <a href="https://www.nesdev.org/wiki/CPU_power_up_state" class="postlink">CPU and APU power up state</a> for info on specific registers.<br>

<blockquote cite="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120"><div><cite><a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">EvtimDjerekarov</a> wrote: <a href="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120" aria-label="View quoted post" data-post-id="297120" onclick="if(document.getElementById(hash.substr(1)))href=hash"><i class="icon fa-arrow-circle-up fa-fw" aria-hidden="true"></i></a><span class="responsive-hide">Tue Nov 12, 2024 3:43 am</span></cite>
4. How does the current sample address wrap around when incremented?
</div></blockquote>

It wraps around to $8000. This is the only way to load sample data from addresses below $C000.<br>

<blockquote cite="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120"><div><cite><a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">EvtimDjerekarov</a> wrote: <a href="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120" aria-label="View quoted post" data-post-id="297120" onclick="if(document.getElementById(hash.substr(1)))href=hash"><i class="icon fa-arrow-circle-up fa-fw" aria-hidden="true"></i></a><span class="responsive-hide">Tue Nov 12, 2024 3:43 am</span></cite>
- if looping is disabled, is IRQ raised when the last sample bit is shifted out to the DAC counter (all 8 bits are shifted out and ReaderBuffer is empty)?
</div></blockquote>

I don't know the exact timing of DMC IRQ to the cycle, but it occurs when bytes remaining decrements to 0. That means the read buffer contains the last byte of the sample and the second to last byte (if applicable) is being played.<br>

<blockquote cite="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120"><div><cite><a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">EvtimDjerekarov</a> wrote: <a href="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120" aria-label="View quoted post" data-post-id="297120" onclick="if(document.getElementById(hash.substr(1)))href=hash"><i class="icon fa-arrow-circle-up fa-fw" aria-hidden="true"></i></a><span class="responsive-hide">Tue Nov 12, 2024 3:43 am</span></cite>
- If looping is enabled, when does the first DMA of the second sample repetition begin (will there be a one DMA worth of time "gap" between repetitions)?
</div></blockquote>

There is no gap. If bytes remaining becomes 0 and looping is enabled, it reloads the sample address and length from the APU registers and DMAs the first byte of this 'new' sample. (Note that looping doesn't guarantee the same data is played; you can change these registers so that it starts the loop with a different sample).<br>

<blockquote cite="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120"><div><cite><a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">EvtimDjerekarov</a> wrote: <a href="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120" aria-label="View quoted post" data-post-id="297120" onclick="if(document.getElementById(hash.substr(1)))href=hash"><i class="icon fa-arrow-circle-up fa-fw" aria-hidden="true"></i></a><span class="responsive-hide">Tue Nov 12, 2024 3:43 am</span></cite>
- what happens to the ReaderBuffer and ShiftRegister if playback is stopped in the middle of a sample? Can it be resumed or it can only be restarted?
</div></blockquote>

Playback of a byte cannot be stopped and it will still play the next byte in the buffer (the byte fetched by DMA but not yet being played). I don't know the mechanism for how a $4015 write stops a sample (I'm still trying to trace this out in Breaks), but I believe it's equivalent to setting bytes_remaining to 0. However, there is some complexity here because of the 'explicit stop' DMC DMA bug discussed on the wiki where there's a delay in the sample being stopped that can cause a DMA to start and then abort, but this is a pretty advanced issue that almost nobody emulates. (If you need a reference, Mesen emulates these bugs and DMA in general extremely well.)<br>

<blockquote cite="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120"><div><cite><a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">EvtimDjerekarov</a> wrote: <a href="./viewtopic.php?p=297120&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297120" aria-label="View quoted post" data-post-id="297120" onclick="if(document.getElementById(hash.substr(1)))href=hash"><i class="icon fa-arrow-circle-up fa-fw" aria-hidden="true"></i></a><span class="responsive-hide">Tue Nov 12, 2024 3:43 am</span></cite>
- What means to "restart" the current sample. A new DMA to fetch its first byte will begin as soon as ShiftRegister has BitsRemaining = 0 and ReaderBuffer is empty?
</div></blockquote>

If you write to $4015 with the DMC bit set while bytes_remaining is 0, then the sample address is loaded from $4012 and bytes_remaining from $4013. The first sample byte will then be fetched immediately if the read buffer is empty (what I call a load DMA), or after the buffer becomes empty (a reload DMA). If bytes_remaining is nonzero, this write does nothing.</div>

			
			
									
						
										
						</div>

		</div>

				<div class="back2top">
						<a href="#top" class="top" title="Top">
				<i class="icon fa-chevron-circle-up fa-fw icon-gray" aria-hidden="true"></i>
				<span class="sr-only">Top</span>
			</a>
					</div>
		
		</div>
	</div>

				<div id="p297131" class="post has-profile bg2">
		<div class="inner">

		<dl class="postprofile" id="profile297131">
			<dt class="no-profile-rank no-avatar">
				<div class="avatar-container">
																			</div>
								<a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="username">EvtimDjerekarov</a>							</dt>

									
		<dd class="profile-posts"><strong>Posts:</strong> <a href="./search.php?author_id=3511&amp;sr=posts&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">9</a></dd>		<dd class="profile-joined"><strong>Joined:</strong> Sun Apr 13, 2008 5:58 am</dd>		
		
						
						
		</dl>

		<div class="postbody">
						<div id="post_content297131">

					<h3 >
						<a href="./viewtopic.php?p=297131&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297131">Re: DMC/DMA timing and quirks</a>
		</h3>

													<ul class="post-buttons">
																																									<li>
							<a href="./posting.php?mode=quote&amp;p=297131&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" title="Reply with quote" class="button button-icon-only">
								<i class="icon fa-quote-left fa-fw" aria-hidden="true"></i><span class="sr-only">Quote</span>
							</a>
						</li>
														</ul>
							
						<p class="author">
									<a class="unread" href="./viewtopic.php?p=297131&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297131" title="Post">
						<i class="icon fa-file fa-fw icon-lightgray icon-md" aria-hidden="true"></i><span class="sr-only">Post</span>
					</a>
								<span class="responsive-hide">by <strong><a href="./memberlist.php?mode=viewprofile&amp;u=3511&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="username">EvtimDjerekarov</a></strong> &raquo; </span><time datetime="2024-11-13T10:42:32+00:00">Wed Nov 13, 2024 3:42 am</time>
			</p>
			
			
			
			
			<div class="content">Thanks for the clarifications!<br>
<br>
I still have some problems though, and cannot find their source. I had a look on Mesen's source code and there are some really obscure timing things implemented.<br>
<br>
With my attempts, I can get the <strong class="text-strong">dpcmletterbox</strong> test run correctly, if i schedule DMA when the <strong class="text-strong">ReadBuffer</strong> is empty AND <strong class="text-strong">bitsRemaining == 0</strong> in the <strong class="text-strong">Shifter</strong>. Otherwise, if I just schedule a DMA on ANY CPU clock cycle when the <strong class="text-strong">ReadBuffer</strong> is empty and <strong class="text-strong">BytesRemaining &gt; 0</strong>, then the lower border in the test disappears.<br>
<br>
With <strong class="text-strong">sprdma_and_dmc_dma</strong> test, things look even more awkward. If I use the first aforementioned DMC invoke method, results show something like ~3778 cycles. If i use the second IRQ invoke method - ~114 cycles - less than the duration of the Sprite DMA!<br>
<br>
Can someone explain in greater detail what does the <strong class="text-strong">sprdma_and_dmc_dma</strong> test actually do. I still do not have a debugger in mu EMU to trace it, and if i know the way the test works, this may be very revealing.</div>

			
			
									
						
										
						</div>

		</div>

				<div class="back2top">
						<a href="#top" class="top" title="Top">
				<i class="icon fa-chevron-circle-up fa-fw icon-gray" aria-hidden="true"></i>
				<span class="sr-only">Top</span>
			</a>
					</div>
		
		</div>
	</div>

				<div id="p297132" class="post has-profile bg1">
		<div class="inner">

		<dl class="postprofile" id="profile297132">
			<dt class="has-profile-rank no-avatar">
				<div class="avatar-container">
																			</div>
								<a href="./memberlist.php?mode=viewprofile&amp;u=8835&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="username">Fiskbit</a>							</dt>

						<dd class="profile-rank">Site Admin</dd>			
		<dd class="profile-posts"><strong>Posts:</strong> <a href="./search.php?author_id=8835&amp;sr=posts&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">1442</a></dd>		<dd class="profile-joined"><strong>Joined:</strong> Sat Nov 18, 2017 9:15 pm</dd>		
		
						
						
		</dl>

		<div class="postbody">
						<div id="post_content297132">

					<h3 >
						<a href="./viewtopic.php?p=297132&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297132">Re: DMC/DMA timing and quirks</a>
		</h3>

													<ul class="post-buttons">
																																									<li>
							<a href="./posting.php?mode=quote&amp;p=297132&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" title="Reply with quote" class="button button-icon-only">
								<i class="icon fa-quote-left fa-fw" aria-hidden="true"></i><span class="sr-only">Quote</span>
							</a>
						</li>
														</ul>
							
						<p class="author">
									<a class="unread" href="./viewtopic.php?p=297132&amp;sid=5673b41f701d34d4a2afb3e98bb483e5#p297132" title="Post">
						<i class="icon fa-file fa-fw icon-lightgray icon-md" aria-hidden="true"></i><span class="sr-only">Post</span>
					</a>
								<span class="responsive-hide">by <strong><a href="./memberlist.php?mode=viewprofile&amp;u=8835&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="username">Fiskbit</a></strong> &raquo; </span><time datetime="2024-11-13T11:58:54+00:00">Wed Nov 13, 2024 4:58 am</time>
			</p>
			
			
			
			
			<div class="content">DMC DMA definitely doesn't care about the state of the playback buffer. It only cares about the read buffer and will fill it if it's empty. Similarly, the playback unit doesn't even care if the DMC channel is enabled; if there is a byte in the read buffer, it will play it. They are both very simple, greedy devices.<br>
<br>
For the sprdma_and_dmc_dma test, it looks like each iteration moves a DMC DMA closer to the start of an OAM DMA until the two collide. The first 5 iterations have the DMC DMA happen pre-OAM DMA and the remaining iterations have it occur during OAM DMA. DMA units don't interfere with each other unless they're both trying to access on the same cycle, in which case DMC DMA wins. This means DMC DMA only adds 2 cycles during these iterations instead of its normal 4. Specifically, DMC DMA reloads normally do halt, dummy, alignment, read, costing 4 cycles. The CPU is already halted during the first 3 due to OAM DMA, which is doing its reads and writes as normal, so they have no cost here. Then OAM DMA is suspended for 1 cycle while DMC DMA reads. Finally, OAM DMA resumes, but is misaligned, so it spends 1 cycle aligning before continuing with reads and writes as usual. So, the DMC DMA itself only takes 1 cycle, but it also adds 1 cycle to OAM DMA.<br>
<br>
The examples in this section show this exact behavior, cycle by cycle: <a href="https://www.nesdev.org/wiki/DMA#DMC_DMA_during_OAM_DMA" class="postlink">https://www.nesdev.org/wiki/DMA#DMC_DMA_during_OAM_DMA</a>.</div>

			
			
									
						
										
						</div>

		</div>

				<div class="back2top">
						<a href="#top" class="top" title="Top">
				<i class="icon fa-chevron-circle-up fa-fw icon-gray" aria-hidden="true"></i>
				<span class="sr-only">Top</span>
			</a>
					</div>
		
		</div>
	</div>

	

	<div class="action-bar bar-bottom">
	
			<a href="./posting.php?mode=reply&amp;t=25574&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="button" title="Post a reply">
							<span>Post Reply</span> <i class="icon fa-reply fa-fw" aria-hidden="true"></i>
					</a>
		
		<div class="dropdown-container dropdown-button-control topic-tools">
		<span title="Topic tools" class="button button-secondary dropdown-trigger dropdown-select">
			<i class="icon fa-wrench fa-fw" aria-hidden="true"></i>
			<span class="caret"><i class="icon fa-sort-down fa-fw" aria-hidden="true"></i></span>
		</span>
		<div class="dropdown">
			<div class="pointer"><div class="pointer-inner"></div></div>
			<ul class="dropdown-contents">
																												<li>
					<a href="./viewtopic.php?t=25574&amp;sid=5673b41f701d34d4a2afb3e98bb483e5&amp;view=print" title="Print view" accesskey="p">
						<i class="icon fa-print fa-fw" aria-hidden="true"></i><span>Print view</span>
					</a>
				</li>
											</ul>
		</div>
	</div>

			<form method="post" action="./viewtopic.php?t=25574&amp;sid=5673b41f701d34d4a2afb3e98bb483e5">
		<div class="dropdown-container dropdown-container-left dropdown-button-control sort-tools">
	<span title="Display and sorting options" class="button button-secondary dropdown-trigger dropdown-select">
		<i class="icon fa-sort-amount-asc fa-fw" aria-hidden="true"></i>
		<span class="caret"><i class="icon fa-sort-down fa-fw" aria-hidden="true"></i></span>
	</span>
	<div class="dropdown hidden">
		<div class="pointer"><div class="pointer-inner"></div></div>
		<div class="dropdown-contents">
			<fieldset class="display-options">
							<label>Display: <select name="st" id="st"><option value="0" selected="selected">All posts</option><option value="1">1 day</option><option value="7">7 days</option><option value="14">2 weeks</option><option value="30">1 month</option><option value="90">3 months</option><option value="180">6 months</option><option value="365">1 year</option></select></label>
								<label>Sort by: <select name="sk" id="sk"><option value="a">Author</option><option value="t" selected="selected">Post time</option><option value="s">Subject</option></select></label>
				<label>Direction: <select name="sd" id="sd"><option value="a" selected="selected">Ascending</option><option value="d">Descending</option></select></label>
								<hr class="dashed" />
				<input type="submit" class="button2" name="sort" value="Go" />
						</fieldset>
		</div>
	</div>
</div>
		</form>
	
	
	
			<div class="pagination">
			4 posts
							&bull; Page <strong>1</strong> of <strong>1</strong>
					</div>
	</div>


<div class="action-bar actions-jump">
		<p class="jumpbox-return">
		<a href="./viewforum.php?f=3&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="left-box arrow-left" accesskey="r">
			<i class="icon fa-angle-left fa-fw icon-black" aria-hidden="true"></i><span>Return to “NESemdev”</span>
		</a>
	</p>
	
		<div class="jumpbox dropdown-container dropdown-container-right dropdown-up dropdown-left dropdown-button-control" id="jumpbox">
			<span title="Jump to" class="button button-secondary dropdown-trigger dropdown-select">
				<span>Jump to</span>
				<span class="caret"><i class="icon fa-sort-down fa-fw" aria-hidden="true"></i></span>
			</span>
		<div class="dropdown">
			<div class="pointer"><div class="pointer-inner"></div></div>
			<ul class="dropdown-contents">
																				<li><a href="./viewforum.php?f=24&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-cat-link"> <span> NES / Famicom</span></a></li>
																<li><a href="./viewforum.php?f=2&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; NESdev</span></a></li>
																<li><a href="./viewforum.php?f=3&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; NESemdev</span></a></li>
																<li><a href="./viewforum.php?f=21&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; NES Graphics</span></a></li>
																<li><a href="./viewforum.php?f=6&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; NES Music</span></a></li>
																<li><a href="./viewforum.php?f=22&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; Homebrew Projects</span></a></li>
																<li><a href="./viewforum.php?f=40&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; 2023 NESdev Competition</span></a></li>
																<li><a href="./viewforum.php?f=39&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; 2022 NESdev Competition</span></a></li>
																<li><a href="./viewforum.php?f=36&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; 2020 NESdev Competition</span></a></li>
																<li><a href="./viewforum.php?f=35&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; 2019 NESdev Competition</span></a></li>
																<li><a href="./viewforum.php?f=34&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; 2018 NESdev Competition</span></a></li>
																<li><a href="./viewforum.php?f=33&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; 2017 NESdev Competition</span></a></li>
																<li><a href="./viewforum.php?f=32&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; 2016 NESdev Competition</span></a></li>
																<li><a href="./viewforum.php?f=31&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; 2014 NESdev Competition</span></a></li>
																<li><a href="./viewforum.php?f=30&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; 2011 NESdev Competition</span></a></li>
																<li><a href="./viewforum.php?f=10&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; Newbie Help Center</span></a></li>
																<li><a href="./viewforum.php?f=9&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; NES Hardware and Flash Equipment</span></a></li>
																<li><a href="./viewforum.php?f=28&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; Reproduction</span></a></li>
																<li><a href="./viewforum.php?f=19&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; NESdev International</span></a></li>
																<li><a href="./viewforum.php?f=11&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; FCdev</span></a></li>
																<li><a href="./viewforum.php?f=18&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; NESdev China</span></a></li>
																<li><a href="./viewforum.php?f=37&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-cat-link"> <span> SNES / SFC</span></a></li>
																<li><a href="./viewforum.php?f=12&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; SNESdev</span></a></li>
																<li><a href="./viewforum.php?f=38&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; SNESdev Homebrew Projects</span></a></li>
																<li><a href="./viewforum.php?f=25&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-cat-link"> <span> Other</span></a></li>
																<li><a href="./viewforum.php?f=23&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; Other Retro Dev</span></a></li>
																<li><a href="./viewforum.php?f=20&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span><span class="spacer"></span> <span>&#8627; &nbsp; GBDev</span></a></li>
																<li><a href="./viewforum.php?f=5&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; General Stuff</span></a></li>
																<li><a href="./viewforum.php?f=4&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; Membler Industries</span></a></li>
																<li><a href="./viewforum.php?f=15&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; Test Forum</span></a></li>
																<li><a href="./viewforum.php?f=26&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-cat-link"> <span> Site Issues</span></a></li>
																<li><a href="./viewforum.php?f=13&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; phpBB Issues</span></a></li>
																<li><a href="./viewforum.php?f=14&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; Web Issues</span></a></li>
																<li><a href="./viewforum.php?f=16&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" class="jumpbox-sub-link"><span class="spacer"></span> <span>&#8627; &nbsp; Wiki Issues</span></a></li>
											</ul>
		</div>
	</div>

	</div>




			</div>


<div id="page-footer" class="page-footer" role="contentinfo">
	<div class="navbar" role="navigation">
	<div class="inner">

	<ul id="nav-footer" class="nav-footer linklist" role="menubar">
		<li class="breadcrumbs">
									<span class="crumb"><a href="./index.php?sid=5673b41f701d34d4a2afb3e98bb483e5" data-navbar-reference="index"><i class="icon fa-home fa-fw" aria-hidden="true"></i><span>Board index</span></a></span>					</li>
		
				<li class="rightside">All times are <span title="UTC-7">UTC-07:00</span></li>
							<li class="rightside">
				<a href="/app.php/user/delete_cookies?sid=5673b41f701d34d4a2afb3e98bb483e5" data-ajax="true" data-refresh="true" role="menuitem">
					<i class="icon fa-trash fa-fw" aria-hidden="true"></i><span>Delete cookies</span>
				</a>
			</li>
																<li class="rightside" data-last-responsive="true">
				<a href="./memberlist.php?mode=contactadmin&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" role="menuitem">
					<i class="icon fa-envelope fa-fw" aria-hidden="true"></i><span>Contact us</span>
				</a>
			</li>
			</ul>

	</div>
</div>

	<div class="copyright">
				<p class="footer-row">
			<span class="footer-copyright">Powered by <a href="https://www.phpbb.com/">phpBB</a>&reg; Forum Software &copy; phpBB Limited</span>
		</p>
		<p class="footer-row">
			Prosilver Dark Edition by <a href="http://www.planetstyles.net/">Premium phpBB Styles</a>
		</p>
						<p class="footer-row" role="menu">
			<a class="footer-link" href="./ucp.php?mode=privacy&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" title="Privacy" role="menuitem">
				<span class="footer-link-text">Privacy</span>
			</a>
			|
			<a class="footer-link" href="./ucp.php?mode=terms&amp;sid=5673b41f701d34d4a2afb3e98bb483e5" title="Terms" role="menuitem">
				<span class="footer-link-text">Terms</span>
			</a>
		</p>
					</div>

	<div id="darkenwrapper" class="darkenwrapper" data-ajax-error-title="AJAX error" data-ajax-error-text="Something went wrong when processing your request." data-ajax-error-text-abort="User aborted request." data-ajax-error-text-timeout="Your request timed out; please try again." data-ajax-error-text-parsererror="Something went wrong with the request and the server returned an invalid reply.">
		<div id="darken" class="darken">&nbsp;</div>
	</div>

	<div id="phpbb_alert" class="phpbb_alert" data-l-err="Error" data-l-timeout-processing-req="Request timed out.">
		<a href="#" class="alert_close">
			<i class="icon fa-times-circle fa-fw" aria-hidden="true"></i>
		</a>
		<h3 class="alert_title">&nbsp;</h3><p class="alert_text"></p>
	</div>
	<div id="phpbb_confirm" class="phpbb_alert">
		<a href="#" class="alert_close">
			<i class="icon fa-times-circle fa-fw" aria-hidden="true"></i>
		</a>
		<div class="alert_text"></div>
	</div>
</div>

</div>

<div>
	<a id="bottom" class="anchor" accesskey="z"></a>
	</div>

<script src="./assets/javascript/jquery-3.7.1.min.js?assets_version=61"></script>
<script src="./assets/javascript/core.js?assets_version=61"></script>


<script>
(function($) {  // Avoid conflicts with other libraries

'use strict';

	phpbb.addAjaxCallback('reporttosfs', function(data) {
		if (data.postid !== "undefined") {
			$('#sfs' + data.postid).hide();
			phpbb.closeDarkenWrapper(5000);
		}
	});

})(jQuery);
</script>
<script src="./styles/prosilver/template/forum_fn.js?assets_version=61"></script>
<script src="./styles/prosilver/template/ajax.js?assets_version=61"></script>



<script defer src="https://static.cloudflareinsights.com/beacon.min.js/vcd15cbe7772f49c399c6a5babf22c1241717689176015" integrity="sha512-ZpsOmlRQV6y907TI0dKBHq9Md29nnaEIPlkf84rnaERnq6zvWvPUqr2ft8M1aS28oN72PdrCzSjY4U6VaAw1EQ==" data-cf-beacon='{"version":"2024.11.0","token":"6c2a51884de146568600589c93ac5eda","r":1,"server_timing":{"name":{"cfCacheStatus":true,"cfEdge":true,"cfExtPri":true,"cfL4":true,"cfOrigin":true,"cfSpeedBrain":true},"location_startswith":null}}' crossorigin="anonymous"></script>
<script>(function(){function c(){var b=a.contentDocument||a.contentWindow.document;if(b){var d=b.createElement('script');d.innerHTML="window.__CF$cv$params={r:'9d1c9f833a6ef07f',t:'MTc3MTc0MzgwMA=='};var a=document.createElement('script');a.src='/cdn-cgi/challenge-platform/scripts/jsd/main.js';document.getElementsByTagName('head')[0].appendChild(a);";b.getElementsByTagName('head')[0].appendChild(d)}}if(document.body){var a=document.createElement('iframe');a.height=1;a.width=1;a.style.position='absolute';a.style.top=0;a.style.left=0;a.style.border='none';a.style.visibility='hidden';document.body.appendChild(a);if('loading'!==document.readyState)c();else if(window.addEventListener)document.addEventListener('DOMContentLoaded',c);else{var e=document.onreadystatechange||function(){};document.onreadystatechange=function(b){e(b);'loading'!==document.readyState&&(document.onreadystatechange=e,c())}}}})();</script></body>
</html>
